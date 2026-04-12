# Prime Examples

## Normal cases

* derive: only phrasal verbs.
* namely: single word class.
* dog: 2 word classes.
* ??: 3 word classes.
* ??: whole word has single formal meaning.
* awake: whole word marked as formal but has multiple meanings.
* namely: single meaning.
* dog: multiple meanings.
* dog: CEFR marked to meaning.
* dog: CEFR marked only to topic instead of meaning.

## Special cases

* advice: the grammar tagged on entry level instead of the sense level.
* afraid: should extract `class="cf"`
* amazing: multiple `class="cf"` but not attached to the sense. They're attached to examples instead.
* blonde: should extract `class="dis-g"`.

## Exception cases

* clean: broken source.
* wake: broken source.

## Known failure cases

```
Name=big
WordClass=verb
IsOnlyPhrasalVerb=False

Name=buddy
WordClass=verb
IsOnlyPhrasalVerb=False

Name=glory
WordClass=verb
IsOnlyPhrasalVerb=False

Name=hot
WordClass=verb
IsOnlyPhrasalVerb=False

Name=kit
WordClass=verb
IsOnlyPhrasalVerb=False

Name=leg
WordClass=verb
IsOnlyPhrasalVerb=False

Name=rat
WordClass=verb
IsOnlyPhrasalVerb=False

Name=sake
WordClass=noun
IsOnlyPhrasalVerb=False

Name=span
WordClass=adjective
IsOnlyPhrasalVerb=False

Name=sum
WordClass=verb
IsOnlyPhrasalVerb=False
```

## HTML analysis

`ol class="sense_single"` if there was only single meanings.

`ol class="senses_multiple"` if there was multiple meanings.

Can also be determined by entries `div id="entryContent"`.

`<h1 class="headword" htag="h1" id="dog_h_2" random="y" hclass="headword">dog</h1> <span class="pos" hclass="pos" htag="span">verb</span>` present word class.

### Layouts

1. `top-container`: metadata.
    1.  `h1 class="headword"` contains the word name.
    2.  `span class="pos"` contains the word class.
    3.  Sometimes `span class="labels"` contains the labels for all senses.
2. `sense_single`/`senses_multiple` both are ordered list, each item in it contains a sense.
    1. The `li` could be attributed with
        1. `sensenum` only for multiple senses.
        2. `fkox3000`/`fkox5000` in Oxford 3000/5000        wordlist.
        3. `fkcefr` the CEFR level.
    2. `span class="def"` contains the English meaning.
    3. The following `defT` contains the translated   meaning.
    4. The following `ul class="examples"` contains the   example sentences.
    5. Should ignore things after `defT` for meanings.
    6. Could appear some labels before `def`. `<span class="grammar" hclass="grammar" htag="span">[plural]</span> <span class="labels" htag="span" hclass="labels">(British English, informal)</span>`
3. `phrasal_verb_links`
