module PrintedFingering

// One column, one side, already identified single digits. Staff height
// increases upwards. Owner is an event's voice number, not its source ID.
sig Head { height: one Int, voices: some Int }
sig Digit { height: one Int }
enum Side { Above, Below }
one sig Column { side: one Side }
fact SeparateRanks {
  Head.voices in 1 + 2 + 3 + 4
  all disj a,b: Head | a.height != b.height
  all disj a,b: Digit | a.height != b.height
}
pred ahead[a,b: Int] { Column.side = Above => a > b else a < b }
fun match[d: Digit]: set Head {
  {h: Head | #{k: Head | ahead[k.height,h.height]} =
    #{k: Digit | ahead[k.height,d.height]}}
}
fun owner[h: Head]: set Int { {v: h.voices | no w: h.voices | w < v} }
assert RankedDigits {
  #Digit <= #Head implies {
    all d: Digit | one match[d] and one owner[match[d]]
    all disj d,e: Digit | ahead[d.height,e.height] implies
      ahead[match[d].height,match[e].height]
  }
  #Digit > #Head implies some d: Digit | no match[d]
}
SharedLowerVoice: run { some d: Digit | let h = match[d] |
  h.voices = 2 + 4 and owner[h] = 2 } for 3 but 4 Int expect 1
check RankedDigits for 3 but 4 Int expect 0
