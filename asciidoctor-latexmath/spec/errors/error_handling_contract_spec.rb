# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe Asciidoctor::Latexmath::LatexmathError do
  it "inherits from StandardError" do
    expect(described_class.superclass).to eq(StandardError)
  end
end

RSpec.describe "Error handling policies" do
  it "supports abort and log policies" do
    abort_policy = Asciidoctor::Latexmath::ErrorHandling.policy(:abort)
    log_policy = Asciidoctor::Latexmath::ErrorHandling.policy(:log)

    expect(abort_policy).to be_abort
    expect(log_policy).to be_log
  end

  it "raises on invalid policy" do
    expect { Asciidoctor::Latexmath::ErrorHandling.policy(:noop) }
      .to raise_error(ArgumentError)
  end
end

RSpec.describe "Error classes" do
  let(:error_classes) do
    {
      unsupported_format: Asciidoctor::Latexmath::UnsupportedFormatError,
      missing_tool: Asciidoctor::Latexmath::MissingToolError,
      invalid_attribute: Asciidoctor::Latexmath::InvalidAttributeError,
      unsupported_value: Asciidoctor::Latexmath::UnsupportedValueError,
      target_conflict: Asciidoctor::Latexmath::TargetConflictError,
      render_timeout: Asciidoctor::Latexmath::RenderTimeoutError,
      stage_failure: Asciidoctor::Latexmath::StageFailureError
    }
  end

  it "defines each custom error inheriting from LatexmathError" do
    error_classes.each_value do |klass|
      expect(klass).to be < Asciidoctor::Latexmath::LatexmathError
    end
  end
end

RSpec.describe Asciidoctor::Latexmath::ErrorHandling::Placeholder do
  it "renders a multi-line placeholder string" do
    latex_source = <<~LATEX
      \\documentclass{standalone}
      \\begin{document}
      \\[
      x
      \\]
      \\end{document}
    LATEX

    html = described_class.render(
      message: "Missing tool",
      command: "pdflatex foo.tex",
      stdout: "",
      stderr: "error",
      source: "latexmath:[x]",
      latex_source: latex_source
    )

    # No changes needed, lines are already correct
    expect(html).to include("highlight latexmath-error")
    expect(html.lines.first.strip).to eq(%(<pre class="highlight latexmath-error" role="note" data-latex-error="1">))
    expect(html).to include("Source (LaTeX): \\documentclass{standalone}")
  end
end
