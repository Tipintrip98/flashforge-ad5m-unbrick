; Stark HUD - stili condivisi (MeterStyle)

[sCard]
; Scheda "vetro" stile HyperOS / Apple: angoli ampi, filo ciano, riflesso superiore
Shape=Rectangle 0,0,(#CardW#*#Scale#),(#CardH#*#Scale#),(22*#Scale#) | Fill Color #Glass# | StrokeWidth 1 | Stroke Color #Line#
Shape2=Rectangle (26*#Scale#),0,((#CardW#-52)*#Scale#),1 | Fill LinearGradient TopGlow | StrokeWidth 0
TopGlow=180 | 56,214,255,0 ; 0.0 | 56,214,255,170 ; 0.5 | 56,214,255,0 ; 1.0
AntiAlias=1

[sTitle]
FontFace=#FontDisplay#
FontWeight=600
FontSize=(10*#Scale#)
FontColor=#Accent#
StringCase=Upper
InlineSetting=CharacterSpacing | 3 | 3
AntiAlias=1

[sMono]
FontFace=#FontMono#
FontSize=(7.5*#Scale#)
FontColor=#Muted#
InlineSetting=CharacterSpacing | 1.5 | 1.5
AntiAlias=1

[sText]
FontFace=#FontDisplay#
FontWeight=500
FontSize=(11*#Scale#)
FontColor=#Text#
AntiAlias=1

[sValue]
FontFace=#FontMono#
FontSize=(8.5*#Scale#)
FontColor=#Text#
StringAlign=Right
AntiAlias=1
