#!/usr/bin/env python3
"""Stark HUD - generatore dello schema suoni.

Sintetizza suoni di sistema discreti (stile "vetro" Apple + "power-up" HUD)
senza campioni esterni: solo numpy. Output WAV 44.1 kHz / 16 bit / stereo.

Uso:  python3 tools/gen_sounds.py      (dalla cartella windows-stark-ui)
"""
import os
import wave

import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "theme", "sounds")
RNG = np.random.default_rng(3000)


def t_axis(dur):
    return np.arange(int(SR * dur)) / SR


def env(n, attack=0.004, decay=0.35):
    t = np.arange(n) / SR
    a = np.clip(t / attack, 0, 1)
    return a * np.exp(-t / decay)


def bell(freq, dur, decay=0.35, bright=1.0):
    """Campanella "vetro": parziali leggermente inarmoniche, attacco morbido."""
    t = t_axis(dur)
    partials = [(1.0, 1.0, 1.0), (2.0, 0.28 * bright, 0.55), (2.76, 0.12 * bright, 0.4), (5.4, 0.04 * bright, 0.22)]
    s = np.zeros_like(t)
    for ratio, amp, dk in partials:
        s += amp * np.sin(2 * np.pi * freq * ratio * t) * env(len(t), 0.003, decay * dk)
    return s


def sweep(f0, f1, dur, shape="exp"):
    t = t_axis(dur)
    if shape == "exp":
        f = f0 * (f1 / f0) ** (t / dur)
    else:
        f = f0 + (f1 - f0) * t / dur
    phase = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(phase)


def lowpass(x, cutoff):
    """Passa-basso a un polo; cutoff puo' essere un array (filtro che si muove)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape)
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def place(buf, sig, at):
    i = int(at * SR)
    end = min(len(buf), i + len(sig))
    buf[i:end] += sig[: end - i]
    return buf


def reverb(x, mix=0.22, room=0.82, spread=0):
    """Riverbero Schroeder (4 comb + 2 allpass), leggero e "arioso"."""
    combs = [1557, 1617, 1491, 1422]
    allp = [225, 556]
    out = np.zeros(len(x) + SR)
    xin = np.concatenate([x, np.zeros(SR)])
    for d in combs:
        d += spread
        y = np.zeros_like(xin)
        for i in range(len(xin)):
            y[i] = xin[i] + (room * y[i - d] if i >= d else 0.0)
        out += y
    out /= len(combs)
    for d in allp:
        d += spread // 3
        y = np.zeros_like(out)
        for i in range(len(out)):
            delayed = out[i - d] if i >= d else 0.0
            yd = y[i - d] if i >= d else 0.0
            y[i] = -0.5 * out[i] + delayed + 0.5 * yd
        out = y
    dry = np.concatenate([x, np.zeros(SR)])
    return (1 - mix) * dry + mix * out


def finish(mono, peak_db=-4.0, mix=0.22, tail=0.6):
    left = reverb(mono, mix=mix, spread=0)
    right = reverb(mono, mix=mix, spread=23)
    n = len(mono) + int(SR * tail)
    st = np.stack([left[:n], right[:n]], axis=1)
    # dissolvenza finale per evitare click
    fade = int(SR * 0.08)
    st[-fade:] *= np.linspace(1, 0, fade)[:, None]
    peak = np.max(np.abs(st)) or 1.0
    st *= (10 ** (peak_db / 20)) / peak
    return st


def write(name, st):
    os.makedirs(OUT, exist_ok=True)
    data = (np.clip(st, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(name, f"{len(st) / SR:.2f}s")


# Note (Hz)
C5, Cs5, E5, Fs5, Gs5, A5, B5 = 523.25, 554.37, 659.25, 739.99, 830.61, 880.0, 987.77
C6, E6, Gs6, G6, B6, E7 = 1046.5, 1318.5, 1661.2, 1568.0, 1975.5, 2637.0


def startup():
    dur = 2.4
    buf = np.zeros(int(SR * dur))
    # ronzio del reattore che sale + soffio filtrato
    hum = sweep(48, 96, 1.25) * 0.5 + sweep(96, 192, 1.25) * 0.25
    hum *= np.linspace(0, 1, len(hum)) ** 1.6
    noise = RNG.standard_normal(len(hum)) * 0.6
    whoosh = lowpass(noise, np.linspace(200, 4200, len(hum))) * np.linspace(0, 1, len(hum)) ** 2
    rise = (hum + whoosh) * np.concatenate([np.ones(len(hum) - 2000), np.linspace(1, 0, 2000)])
    place(buf, rise * 0.55, 0.0)
    # accordo luminoso arpeggiato (E major add9)
    for k, f in enumerate([E5, B5, E6, Gs6, B6]):
        place(buf, bell(f, 1.3, decay=0.55, bright=0.8) * (0.42 - 0.04 * k), 1.12 + 0.045 * k)
    return finish(buf, peak_db=-3.5, mix=0.28, tail=0.9)


def logoff():
    dur = 1.6
    buf = np.zeros(int(SR * dur))
    for k, f in enumerate([B6, Gs6, E6, B5]):
        place(buf, bell(f, 0.9, decay=0.4, bright=0.6) * 0.38, 0.06 * k)
    down = sweep(160, 50, 0.9) * np.linspace(0.6, 0, int(SR * 0.9)) ** 1.5
    place(buf, down * 0.35, 0.25)
    return finish(buf, peak_db=-5.0, mix=0.3, tail=0.8)


def notify():
    buf = np.zeros(int(SR * 0.7))
    place(buf, bell(B5, 0.6, decay=0.3) * 0.5, 0.0)
    place(buf, bell(E6, 0.6, decay=0.32) * 0.55, 0.09)
    return finish(buf, peak_db=-6.0)


def reminder():
    buf = np.zeros(int(SR * 0.9))
    for k, f in enumerate([E6, Gs6, B6]):
        place(buf, bell(f, 0.6, decay=0.3) * 0.5, 0.11 * k)
    return finish(buf, peak_db=-6.0)


def asterisk():
    buf = np.zeros(int(SR * 0.6))
    place(buf, bell(A5, 0.55, decay=0.32) * 0.6, 0.0)
    return finish(buf, peak_db=-7.0)


def exclamation():
    buf = np.zeros(int(SR * 0.6))
    place(buf, bell(Fs5, 0.4, decay=0.18) * 0.6, 0.0)
    place(buf, bell(Fs5, 0.45, decay=0.22) * 0.6, 0.09)
    return finish(buf, peak_db=-6.0)


def error():
    buf = np.zeros(int(SR * 0.7))
    for at, f in [(0.0, E5), (0.12, Cs5)]:
        t = t_axis(0.45)
        tone = np.sin(2 * np.pi * f * t) + 0.18 * np.sin(2 * np.pi * 3 * f * t) + 0.06 * np.sin(2 * np.pi * 5 * f * t)
        place(buf, tone * env(len(t), 0.004, 0.16) * 0.55, at)
    return finish(buf, peak_db=-6.0, mix=0.18)


def default_beep():
    buf = np.zeros(int(SR * 0.25))
    t = t_axis(0.12)
    blip = np.sin(2 * np.pi * 1200 * t) * env(len(t), 0.002, 0.03)
    place(buf, blip * 0.6, 0.0)
    place(buf, bell(E7, 0.15, decay=0.05, bright=0.3) * 0.2, 0.0)
    return finish(buf, peak_db=-9.0, mix=0.15, tail=0.3)


def connect(up=True):
    buf = np.zeros(int(SR * 0.5))
    notes = [C6, G6] if up else [G6, C6]
    for k, f in enumerate(notes):
        place(buf, bell(f, 0.35, decay=0.16) * 0.55, 0.07 * k)
    return finish(buf, peak_db=-7.0, tail=0.4)


def unlock():
    buf = np.zeros(int(SR * 0.6))
    s = sweep(700, 2400, 0.25) * env(int(SR * 0.25), 0.02, 0.12)
    place(buf, s * 0.35, 0.0)
    place(buf, bell(E6, 0.4, decay=0.2, bright=0.5) * 0.4, 0.12)
    return finish(buf, peak_db=-7.0, tail=0.4)


def recycle():
    dur = 0.8
    n = int(SR * dur)
    buf = np.zeros(n)
    noise = RNG.standard_normal(n)
    sweep_lp = lowpass(noise, np.linspace(6000, 300, n)) * env(n, 0.01, 0.22)
    place(buf, sweep_lp * 0.7, 0.0)
    # scintille "dematerializzazione"
    for _ in range(14):
        f = RNG.uniform(2500, 6000)
        place(buf, bell(f, 0.12, decay=0.03, bright=0.2) * 0.12, RNG.uniform(0.0, 0.4))
    return finish(buf, peak_db=-8.0, tail=0.4)


SOUNDS = {
    "startup.wav": startup,
    "logoff.wav": logoff,
    "notify.wav": notify,
    "reminder.wav": reminder,
    "asterisk.wav": asterisk,
    "exclamation.wav": exclamation,
    "error.wav": error,
    "default.wav": default_beep,
    "connect.wav": lambda: connect(True),
    "disconnect.wav": lambda: connect(False),
    "unlock.wav": unlock,
    "recycle.wav": recycle,
}

if __name__ == "__main__":
    for name, fn in SOUNDS.items():
        write(name, fn())
