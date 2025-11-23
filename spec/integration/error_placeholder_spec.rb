# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Error placeholder" do
  it "injects placeholder when on-error=log" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~'ADOC'
          [latexmath, on-error=log]
          ++++
          \error{forced}
          ++++
        ADOC

        RSpec.configuration.reporter.message("NOTE: Seeing 'latexmath rendering failed: forced failure' in the log is expected for this scenario.")

        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(html).to include('class="highlight latexmath-error"')
        expect(html).to include("Source (AsciiDoc)")
        expect(html).to include("Source (LaTeX): \\documentclass[preview")
      end
    end
  end

  it "defaults to log policy when on-error is not provided" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~'ADOC'
          [latexmath]
          ++++
          \error{forced}
          ++++
        ADOC

        RSpec.configuration.reporter.message("NOTE: Seeing 'latexmath rendering failed: forced failure' in the log is expected for this scenario.")
        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(html).to include('class="highlight latexmath-error"')
        expect(html).to include("Command:")
        expect(html).to include("Source (LaTeX): \\documentclass[preview")
      end
    end
  end

  it "raises when on-error is abort" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~'ADOC'
          [latexmath, on-error=abort]
          ++++
          \error{forced}
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::StageFailureError)
      end
    end
  end
end
