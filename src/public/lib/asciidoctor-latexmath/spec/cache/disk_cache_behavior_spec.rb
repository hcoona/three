# frozen_string_literal: true

require "json"
require "tmpdir"
require "fileutils"
require "digest"
require "asciidoctor-latexmath"

RSpec.describe Asciidoctor::Latexmath::Cache::DiskCache do
  let(:cache_root) { Dir.mktmpdir("latexmath-cache-spec-") }
  let(:disk_cache) { described_class.new(cache_root) }
  let(:digest) { "abc123" }
  let(:source_path) { File.join(cache_root, "artifact.src") }
  let(:checksum) { Digest::SHA256.hexdigest("artifact-body") }
  let(:cache_entry) do
    Asciidoctor::Latexmath::Cache::CacheEntry.new(
      final_path: File.join(cache_root, "unused"),
      format: :svg,
      content_hash: "content-hash",
      preamble_hash: "preamble-hash",
      fontsize: "12pt",
      engine: "pdflatex",
      ppi: nil,
      entry_type: :block,
      created_at: Time.now,
      checksum: checksum,
      size_bytes: 13
    )
  end

  before do
    File.write(source_path, "artifact-body")
  end

  after do
    FileUtils.remove_entry(cache_root) if File.exist?(cache_root)
  end

  it "persists metadata with key and checksum" do
    disk_cache.store(digest, cache_entry, source_path)

    entry_root = File.join(cache_root, digest)
    metadata_path = File.join(entry_root, Asciidoctor::Latexmath::Cache::DiskCache::METADATA_FILENAME)
    artifact_path = File.join(entry_root, Asciidoctor::Latexmath::Cache::DiskCache::ARTIFACT_FILENAME)

    metadata = JSON.parse(File.read(metadata_path))

    expect(File).to exist(artifact_path)
    expect(metadata.fetch("key")).to eq(digest)
    expect(metadata.fetch("checksum")).to eq("sha256:#{checksum}")
    expect(metadata.fetch("format")).to eq("svg")
    expect(metadata.fetch("fontsize")).to eq("12pt")
  end

  it "treats checksum mismatch as cache miss" do
    disk_cache.store(digest, cache_entry, source_path)

    artifact_path = File.join(cache_root, digest, Asciidoctor::Latexmath::Cache::DiskCache::ARTIFACT_FILENAME)
    File.write(artifact_path, "tampered")

    expect(disk_cache.fetch(digest)).to be_nil
  end
end
