# Flashforge Adventurer 5M & 5M Pro - Custom Unbrick & Recovery Firmware Kit

[![Flashforge AD5M](https://img.shields.io/badge/Flashforge-Adventurer%205M%20%2F%205M%20Pro-blue.svg)](https://www.flashforge.com/)
[![Firmware Version](https://img.shields.io/badge/Firmware-v5.1.7%20%2F%20v3.2.7-green.svg)]()
[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-Support%20Project-FFDD00.svg?logo=buy-me-a-coffee&logoColor=black)](https://www.buymeacoffee.com/TUO_USERNAME)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Universal low-level recovery and unbrick solution for Flashforge Adventurer 5M and Adventurer 5M Pro 3D printers that are bricked, stuck on the boot screen/logo, or caught in boot loops after official firmware updates.**

---

## 🔍 Root Cause Analysis (Why the official update bricks the printer)

Through reverse engineering of the system startup logs (`ffstartup-arm.log`, `firmwareExe.log`, `dmesg.complete.log`), the exact root cause of the update failure was identified:

1. **The Official `ffstartup-mipsle` Bug**:
   In official Flashforge update packages (`library-2.5.0.tar.xz`), the installation script `run.sh` contains a legacy copy-paste bug from MIPS codebases:
   ```bash
   if [ -f $WORK_DIR/ffstartup-mipsle ];then
       cp_file $WORK_DIR/ffstartup-arm /opt/PROGRAM/
   fi
   ```
   Because the Adventurer 5M runs an **ARMv7** architecture, `$WORK_DIR/ffstartup-mipsle` never exists. Consequently, the core process supervisor `/opt/PROGRAM/ffstartup-arm` is **never updated**.

2. **The `exit status 127` Dynamic Linking Crash**:
   Newer firmware versions (v3.2.7, v5.1.4, v5.1.7) require the NetEase Cloud/NIM shared libraries (`libnim.so`, `libnim_chatroom.so`) located in `/opt/nim/lib`.
   The old supervisor (v1.0.0) has hardcoded library paths that **do not include** `/opt/nim/lib`. When the system tries to execute `firmwareExe`, the Linux dynamic linker fails with:
   `libnim.so: cannot open shared object file: No such file or directory` (`exit status 127`).
   The printer then falls back in a loop to older incompatible software, resulting in a black screen, frozen boot logo, or endless reboot cycles.

3. **Strict Machine Type / PID Check**:
   The standard `flashforge_init.sh` updater aborts if passed arguments do not strictly match internal PID strings, preventing normal USB updates from completing once the system is corrupted.

---

## 🛠️ What This Recovery Kit Fixes

- ✅ **Fixed `library-2.5.0/run.sh`**: Directly installs `/opt/PROGRAM/ffstartup-arm` with executable permissions (`755`).
- ✅ **Dynamic Library Auto-Linking**: Links `/opt/nim/lib/*` directly into `/usr/lib/` to guarantee that `firmwareExe` loads without missing library errors.
- ✅ **Hardware Override (`screwflag`)**: Unconditionally bypasses model/PID mismatch checks.
- ✅ **Direct USB Low-Level Execution**: Uses the native Linux boot hook (`/mnt/flashforge_init.sh`) so you can recover the printer directly from USB without needing the touchscreen or OTA menus.
- ✅ **Filesystem Cleanup**: Cleans corrupted lockfiles and log caches on `/data` that prevent boot.

---

## 📁 Repository Structure

```text
├── 1_USB_DIRECT_FILES/          # Primary v5.1.7 Unbrick files (Ready to copy to USB root)
├── 1_USB_DIRECT_FILES_v5.1.7/   # Standalone copy of v5.1.7 unbrick files
├── 1_USB_DIRECT_FILES_v3.2.7/   # Stable v3.2.7 unbrick files (Fallback)
├── 2_TGZ_UPDATE_PACKAGES/       # Fixed .tgz update packages
│   ├── Adventurer5M-5.1.7-2.2.3-20260605.tgz
│   ├── Adventurer5M-5.1.7-CUSTOM-UNBRICK.tgz
│   ├── Adventurer5M-3.2.7-2.2.3-20251217.tgz
│   └── Adventurer5M-3.2.7-CUSTOM-UNBRICK.tgz
├── GUIDA_UNBRICK_RIPRISTINO.md  # Detailed Italian guide
├── README.txt                   # Quick text instructions
└── README.md                    # English documentation & technical details
```

---


---

## ☕ Support the Project

If this unbrick toolkit saved your printer from an expensive and unnecessary motherboard replacement, consider supporting this project with a small donation! Your support helps maintain and update recovery tools for the 3D printing community.

<p align="center">
  <a href="https://www.buymeacoffee.com/TUO_USERNAME">
    <img src="https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=☕&slug=TUO_USERNAME&button_colour=FFDD00&font_colour=000000&font_family=Cookie&outline_colour=000000&coffee_colour=ffffff" alt="Buy Me A Coffee" />
  </a>
</p>

<p align="center">
  👉 <a href="https://www.buymeacoffee.com/TUO_USERNAME"><b>Support via Buy Me A Coffee (buymeacoffee.com/TUO_USERNAME)</b></a>
</p>

---

## 🚀 How to Unbrick Your Printer (Step-by-Step)

### Requirements:
- A USB flash drive (4 GB to 32 GB).
- Formatted as **FAT32** (MBR).

### Instructions:
1. **Format your USB drive** as FAT32.
2. Open the [`1_USB_DIRECT_FILES/`](1_USB_DIRECT_FILES/) folder.
3. **Copy all 15 files** from `1_USB_DIRECT_FILES/` directly onto the **root** of your USB drive (do **not** place them inside any folder):
   - `flashforge_init.sh`
   - `screwflag`
   - `software-5.1.7.tar.xz`
   - `library-2.5.0.tar.xz`
   - `kernel-2.1.6.tar.xz`
   - `control-2.2.3.tar.xz`
   - `boot.img`, `start.img`, `end.img`
   - `mencoder`, `play`, `inittab`, `shadow`, `freecach.sh`, `Cube-PLA-Test.gx`
4. **Turn off the printer completely** using the power switch on the back.
5. Plug the USB flash drive into the printer's USB port.
6. **Turn on the printer**:
   - The low-level bootloader will detect `flashforge_init.sh` on the USB drive.
   - It will automatically reflash the kernel, mainboard/Eboard control firmware, supervisor, and software.
7. Once the final screen appears or a sound chime plays:
   - **Remove the USB drive**.
   - **Power cycle the printer** (turn off and on).
8. The printer will boot normally with fully functioning firmware!

---

## 📜 License

This recovery toolkit and fixes are released under the [MIT License](LICENSE).
Firmware binaries and assets are property of Zhejiang Flashforge 3D Technology Co., Ltd.
