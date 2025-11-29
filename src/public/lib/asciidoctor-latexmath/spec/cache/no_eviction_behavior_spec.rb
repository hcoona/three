# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Cache eviction" do
  it "does not remove cache entries without explicit action" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-cachedir: cache

          [latexmath]
          ++++
          a_0
          ++++
        ADOC

        convert_with_extension(source)

        cache_root = File.expand_path("cache", Dir.pwd)
        entries = Dir.glob(File.join(cache_root, "*"))
        expect(entries).not_to be_empty

        first_entry = entries.find { |path| File.directory?(path) }
        expect(first_entry).not_to be_nil

        sleep 0.1
        expect(Dir.exist?(first_entry)).to be(true)

        convert_with_extension(source)
        expect(Dir.exist?(first_entry)).to be(true)
      end
    end
  end
end
