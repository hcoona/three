\version "2.24.3"
\paper{
  indent=0\mm
  oddFooterMarkup=##f
  oddHeaderMarkup=##f
  bookTitleMarkup = ##f
  scoreTitleMarkup = ##f
}

\score {
  \new Staff {
    \set Staff.midiInstrument = #"acoustic grand"
    \omit Staff.TimeSignature
    \scoreToInclude
  }
  \layout { }
}
