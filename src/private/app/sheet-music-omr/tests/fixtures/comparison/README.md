# Comparison calibration fixtures

These MusicXML files are self-authored for the calibration of the
[comparison protocol](../../../docs/comparison-protocol.md). They transcribe no
existing score and hold no private content.

- `base.musicxml` is a two-staff piano reference `Y` with the notes, voices
  and marks that most calibration pairs edit. A pair that needs a mark it
  lacks, such as a fermata, fingering, pedal, words or dynamics, adds that
  mark to one or both scores, as its scenario requires.
- `single-staff.musicxml` is a one-staff reference for the barline pair,
  since music21 repeats a barline in each staff of a grand staff.

`tests/test_comparison.py` derives each round trip `T(Y)` from a reference by
exact text edits.
