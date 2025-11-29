# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Output path resolution" do
  it "prefers imagesoutdir when provided" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        build_dir = dir.join("build")
        images_out = build_dir.join("images")
        FileUtils.mkdir_p(images_out)

        source = <<~ADOC
          [latexmath]
          ++++
          a
          ++++
        ADOC

        convert_with_extension(
          source,
          attributes: {
            "outdir" => build_dir.to_s,
            "imagesoutdir" => images_out.to_s,
            "imagesdir" => "assets"
          }
        )

        expect(Dir.glob(images_out.join("*.svg")).size).to eq(1)
      end
    end
  end

  it "falls back to imagesdir when imagesoutdir missing" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        FileUtils.mkdir_p("assets")

        source = <<~ADOC
          [latexmath]
          ++++
          b
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "assets"})

        expect(Dir.glob("assets/*.svg").size).to eq(1)
      end
    end
  end
end
