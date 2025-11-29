# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Conflict detection" do
  it "raises when same basename is used for different formulas" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, foo]
          ++++
          x
          ++++

          [latexmath, foo]
          ++++
          y
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::TargetConflictError) { |error|
          expect(error.message).to include("conflicting target 'images/foo.svg'")
          expect(error.message).to include("hint:")
        }
      end
    end
  end
end
