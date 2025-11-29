# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Cache and artifact controls" do
  it "skips cache storage when nocache option is set" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath%nocache]
          ++++
          a + b
          ++++
        ADOC

        Asciidoctor::Latexmath.reset_render_counters!

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
        first_invocations = Asciidoctor::Latexmath.render_invocations

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
        second_invocations = Asciidoctor::Latexmath.render_invocations

        expect(first_invocations).to eq(1)
        expect(second_invocations).to eq(2)
        expect(Dir.exist?(".asciidoctor/latexmath")).to be(false)
      end
    end
  end

  it "copies tex and log artifacts on success when keep-artifacts is enabled" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-artifacts-dir: artifacts

          [latexmath, options="keep-artifacts"]
          ++++
          c = d
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        tex_files = Dir.glob("artifacts/*.tex")
        log_files = Dir.glob("artifacts/*.log")
        expect(tex_files).not_to be_empty
        expect(log_files).not_to be_empty

        svg_files = Dir.glob("artifacts/*.svg")
        expect(svg_files).not_to be_empty
      end
    end
  end

  it "preserves only tex and log artifacts on failure when on-error=log" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-artifacts-dir: artifacts
          :latexmath-on-error: log

          [latexmath, options="keep-artifacts"]
          ++++
          \\error trigger
          ++++
        ADOC

        RSpec.configuration.reporter.message("NOTE: Seeing 'latexmath rendering failed: forced failure' in the log is expected for this scenario.")
        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})
        expect(html).to include("latexmath-error")

        tex_files = Dir.glob("artifacts/*.tex")
        log_files = Dir.glob("artifacts/*.log")
        other_files = Dir.glob("artifacts/*").reject { |path| path.end_with?(".tex", ".log") }

        expect(tex_files).not_to be_empty
        expect(log_files).not_to be_empty
        expect(other_files).to be_empty
      end
    end
  end
end
