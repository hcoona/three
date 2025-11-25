# Quickstart – asciidoctor-latexmath

## 1. Install
Add to your Gemfile:
```ruby
gem 'asciidoctor', '~> 2.0'
gem 'asciidoctor-latexmath', path: '.' # or published version when released
```
Then:
```bash
bundle install
```

## 2. Minimal Usage
`sample.adoc`:
```adoc
:stem: latexmath
:latexmath-format: svg

[latexmath]
++++
E = mc^2
++++

Inline example: latexmath:[a^2 + b^2 = c^2]
```
Render:
```bash
bundle exec asciidoctor -r ./lib/asciidoctor-latexmath.rb sample.adoc
```

## 3. Selecting Engines & Formats
```adoc
:pdflatex: xelatex
:latexmath-format: png
:latexmath-ppi: 300
```
Per-element override:
```adoc
[latexmath, hyp-eq, svg, pdflatex=tectonic, preamble="\\usepackage{bm}"]
++++
\bm{F} = m a
++++
```

## 4. Cache & Artifacts
- Disable cache globally: `:latexmath-cache: false`
- Disable per block: `[latexmath%nocache]` or `[latexmath, cache=false]`
- Keep intermediate files for a block: `[latexmath, options="keep-artifacts"]`
- Set custom cache dir: `:latexmath-cachedir: build/.cache/latexmath`

## 5. PNG Specific
```adoc
:latexmath-format: png
:latexmath-ppi: 200
```
Or per element: `[latexmath, format=png, ppi=200]`.

## 6. Tool Overrides
```adoc
:latexmath-pdf2svg: /usr/local/bin/dvisvgm
:latexmath-png-tool: pdftoppm
```
Inline override: `latexmath:[x^2,format=svg,pdf2svg=/opt/bin/pdf2svg]`.

## 7. Troubleshooting
| Symptom | Cause | Fix |
|---------|-------|-----|
| Missing tool error | Tool not installed | Install via package manager / adjust PATH |
| Timeout | Complex preamble or hang | Increase `:latexmath-timeout:` or simplify formula |
| Wrong format output | Attribute precedence | Check element `format=` overrides doc setting |
| No cache hit | Changing attributes | Inspect differing cache key components in DEBUG logs |

## 8. Security Notes
- Shell escape disabled by default.
- Preamble trusted (intended for controlled repositories).

## 9. Cleaning Cache
```bash
rm -rf build/.asciidoctor/latexmath
```

## 10. Next Steps
- Explore `research.md` for design decisions.
- Run planned specs once added under `spec/`.
