"""Run the round-trip measurement of the comparison protocol.

The harness prepares the PDMX and OpenScore Lieder samples and the private
gold set, runs each reference score through the reverse and forward converters
and MuseScore, scores the round trips with the comparison instrument and
reports the per-arm figures. Every write goes to the run, temporary or
download directory.
"""
