# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Unsupported attribute values" do
  def expect_actionable_error(source, attributes: {})
    expect {
      convert_with_extension(source, attributes: {"imagesdir" => "images"}.merge(attributes))
    }.to raise_error(Asciidoctor::Latexmath::UnsupportedValueError) { |error|
      yield(error)
      expect(error.message).to include("hint:")
    }

    expect(Dir.exist?("images")).to be(false)
    expect(Dir.exist?(".asciidoctor/latexmath")).to be(false)
  end

  it "raises actionable error when png ppi is outside supported range" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, format=png, ppi=42]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        expect_actionable_error(source) do |error|
          expect(error.message).to include("unsupported attribute: 'ppi=42'")
          expect(error.message).to include("supported: integer between 72 and 600")
          expect(error.message).to include("hint: set ppi between 72 and 600")
        end
      end
    end
  end

  it "raises actionable error when png ppi is not an integer" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, format=png, ppi=abc]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        expect_actionable_error(source) do |error|
          expect(error.message).to include("unsupported attribute: 'ppi=abc'")
          expect(error.message).to include("supported: integer between 72 and 600")
          expect(error.message).to include("hint: set ppi to an integer between 72 and 600")
        end
      end
    end
  end

  it "raises actionable error when timeout is not a positive integer" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, timeout=abc]
          ++++
          E = mc^2
          ++++
        ADOC

        expect_actionable_error(source) do |error|
          expect(error.message).to include("unsupported attribute: 'timeout=abc'")
          expect(error.message).to include("supported: positive integer seconds")
          expect(error.message).to include("hint: set timeout to a positive integer")
        end
      end
    end
  end

  it "raises actionable error when document on-error policy is invalid" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-on-error: explode

          [latexmath]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        expect_actionable_error(source) do |error|
          expect(error.message).to include("unsupported attribute: 'latexmath-on-error=explode'")
          expect(error.message).to include("supported: abort, log")
          expect(error.message).to include("hint: set latexmath-on-error to one of [abort, log]")
        end
      end
    end
  end
end
