module TextScoreTarget

// Fixed crop, header/carry and duration. Slots are ordered token positions,
// not interchangeable note IDs. A missing second line emits no tokens.
abstract sig Token {}
abstract sig VoiceId, Written, Hint extends Token {}
one sig V1, V2 extends VoiceId {}
one sig PlainC, NaturalC, PlainD extends Written {}
one sig Up, Down extends Hint {}
one sig FromZero extends Token {}
abstract sig Slot {}
one sig Opt, IdA, NoteA, StemA, IdB, NoteB, StemB extends Slot {}

pred valid[t: Slot -> Token] {
  t in Opt->FromZero + (IdA + IdB)->VoiceId + (NoteA + NoteB)->Written + (StemA + StemB)->Hint
  all s: Slot | lone t[s]
  one t[IdA] and one t[NoteA] and one t[StemA]
  (some t[IdB] iff some t[NoteB]) and (some t[IdB] iff some t[StemB])
  no t[IdA] & t[IdB]
}
sig Spelling { text: Slot -> lone Token } { valid[text] }

// Interpretation ignores row order and a redundant from=0, but not the
// printed natural, voice assignment or encoded stem direction.
fun reading[t: Slot -> Token]: Token -> Token -> Token {
  t[IdA]->t[NoteA]->t[StemA] + t[IdB]->t[NoteB]->t[StemB]
}
fun canonical[t: Slot -> Token]: Slot -> Token {
  let swap = IdA->IdB + IdB->IdA + NoteA->NoteB + NoteB->NoteA + StemA->StemB + StemB->StemA |
    (t[IdA] = V2 and some t[IdB]) => swap.t else (t - Opt->Token)
}
assert OneLosslessSpelling {
  all a: Spelling | let t = canonical[a.text] | {
    valid[t] and no t[Opt] and (some t[IdB] implies t[IdA] = V1)
    reading[t] = reading[a.text]
    canonical[t] = t
  }
  all a, b: Spelling |
    (canonical[a.text] = canonical[b.text]) iff (reading[a.text] = reading[b.text])
}

// One determined printed event, or two simultaneous events on this staff
// distinguished by their stems. This decision needs the paper, not just syntax.
sig Head { mark: one Written, stem: one Hint }
fact PrintedCase { some Head and #Head <= 2 and (not lone Head implies Head.stem = Hint) }
pred transcribes[t: Slot -> Token] {
  valid[t]
  #reading[t] = #Head
  all h: Head | let v = (one Head or h.stem = Up) => V1 else V2 |
    v->h.mark->h.stem in reading[t]
}
fun twoLines[a, b: set Head]: Slot -> Token {
  Opt->FromZero + IdA->V2 + NoteA->a.mark + StemA->a.stem +
    IdB->V1 + NoteB->b.mark + StemB->b.stem
}
fun target: Slot -> Token {
  canonical[(one Head) => IdA->V1 + NoteA->Head.mark + StemA->Head.stem
                        else twoLines[stem.Down, stem.Up]]
}
assert OneTargetPerDeterminedReading {
  transcribes[target]
  all s: Spelling | transcribes[s.text] implies canonical[s.text] = target
}
DifferentInputsOneTarget: run {
  some disj a, b: Spelling | transcribes[a.text] and transcribes[b.text] and
    a.text[IdA] != b.text[IdA] and some a.text[Opt] and no b.text[Opt] and
    canonical[a.text] = canonical[b.text]
} for 2 expect 1

// In C major both spellings sound C natural. One prints a natural; the
// other doesn't. Holding the voice and stem fixed isolates the distinction.
assert SoundFixesTarget {
  all a, b: Spelling |
    (no a.text[IdB] + b.text[IdB] and a.text[IdA] = b.text[IdA] and
     a.text[StemA] = b.text[StemA] and a.text[NoteA] + b.text[NoteA] = PlainC + NaturalC)
      implies canonical[a.text] = canonical[b.text]
}
check OneLosslessSpelling for 2 expect 0
check OneTargetPerDeterminedReading for 2 expect 0
check SoundFixesTarget for 2 expect 1
