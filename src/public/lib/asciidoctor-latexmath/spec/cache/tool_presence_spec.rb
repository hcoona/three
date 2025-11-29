# frozen_string_literal: true

require "json"
require "asciidoctor-latexmath"

RSpec.describe "Cache tool presence metadata" do
  it "persists selected tool availability in metadata and cache entries" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_lookup(dvisvgm: true, pdf2svg: false, pdftoppm: true, magick: false, gs: false)

        svg_source = <<~ADOC
          [latexmath]
          ++++
          e^{i\pi} + 1 = 0
          ++++
        ADOC

        png_source = <<~ADOC
          :latexmath-format: png

          [latexmath, ppi=200]
          ++++
          e^{i\pi} + 1 = 0
          ++++
        ADOC

        convert_with_extension(svg_source, attributes: {"imagesdir" => "images"})
        convert_with_extension(png_source, attributes: {"imagesdir" => "images"})

        cache_root = File.join(dir, ".asciidoctor", "latexmath")
        entries = Dir.children(cache_root).filter_map do |name|
          entry_dir = File.join(cache_root, name)
          next unless File.directory?(entry_dir)

          metadata_path = File.join(entry_dir, "metadata.json")
          next unless File.file?(metadata_path)

          [name, JSON.parse(File.read(metadata_path))]
        end

        expect(entries).not_to be_empty

        digest_svg, metadata_svg = entries.find { |_, meta| meta["format"] == "svg" }
        digest_png, metadata_png = entries.find { |_, meta| meta["format"] == "png" }

        expect(metadata_svg).not_to be_nil
        expect(metadata_png).not_to be_nil

        expect(metadata_svg.fetch("tool_presence")).to include(
          "dvisvgm" => true,
          "pdf2svg" => false
        )

        expect(metadata_png.fetch("tool_presence")).to include(
          "pdftoppm" => true
        )

        disk_cache = Asciidoctor::Latexmath::Cache::DiskCache.new(cache_root)
        svg_entry = disk_cache.fetch(digest_svg)
        png_entry = disk_cache.fetch(digest_png)

        expect(svg_entry.tool_presence).to include(
          "dvisvgm" => true,
          "pdf2svg" => false
        )

        expect(png_entry.tool_presence).to include(
          "pdftoppm" => true
        )
      end
    end
  end

  def stub_tool_lookup(dvisvgm:, pdf2svg:, pdftoppm:, magick:, gs:)
    Asciidoctor::Latexmath::Rendering::ToolDetector.reset!
    records = {}
    allow(Asciidoctor::Latexmath::Rendering::ToolDetector).to receive(:lookup) do |identifier, command|
      key = [identifier, command]
      records[key] ||= case identifier
      when :dvisvgm
        availability_record(:dvisvgm, dvisvgm, command)
      when :pdf2svg
        availability_record(:pdf2svg, pdf2svg, command)
      when :pdftoppm
        availability_record(:pdftoppm, pdftoppm, command)
      when :magick
        availability_record(:magick, magick, command)
      when :gs
        availability_record(:gs, gs, command)
      else
        Asciidoctor::Latexmath::Rendering::ToolchainRecord.new(id: identifier, available: false, path: command)
      end
    end
  end

  def availability_record(id, available, command)
    path = available ? "/usr/bin/#{id}" : command
    Asciidoctor::Latexmath::Rendering::ToolchainRecord.new(id: id, available: available, path: path)
  end
end
