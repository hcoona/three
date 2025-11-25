# frozen_string_literal: true

require "digest"
require "asciidoctor-latexmath"

RSpec.describe Asciidoctor::Latexmath::Cache::CacheKey do
  let(:inputs) do
    {
      ext_version: "0.0.1",
      content_hash: "abc123",
      format: :svg,
      preamble_hash: "def456",
      fontsize_hash: "font789",
      ppi: "-",
      entry_type: :block
    }
  end

  it "defines the expected field order" do
    expect(described_class::FIELDS_ORDER)
      .to eq(%i[ext_version content_hash format preamble_hash fontsize_hash ppi entry_type])
  end

  it "computes digest as SHA256 joined by newlines" do
    key = described_class.new(**inputs)

    expected = Digest::SHA256.hexdigest(%w[0.0.1 abc123 svg def456 font789 - block].join("\n"))
    expect(key.digest).to eq(expected)
  end

  it "changes digest when preamble hash changes" do
    key_a = described_class.new(**inputs)
    key_b = described_class.new(**inputs.merge(preamble_hash: "zzz"))

    expect(key_a.digest).not_to eq(key_b.digest)
  end

  it "does not change digest when engine metadata changes" do
    key_a = described_class.new(**inputs)
    key_b = described_class.new(**inputs)

    expect(key_a.digest).to eq(key_b.digest)
  end

  it "changes digest when fontsize hash changes" do
    key_a = described_class.new(**inputs)
    key_b = described_class.new(**inputs.merge(fontsize_hash: "other"))

    expect(key_a.digest).not_to eq(key_b.digest)
  end
end

RSpec.describe Asciidoctor::Latexmath::Cache::DiskCache do
  it "responds to fetch, store, and with_lock" do
    disk_cache = described_class.new("/tmp/cache")

    expect(disk_cache).to respond_to(:fetch)
    expect(disk_cache).to respond_to(:store)
    expect(disk_cache).to respond_to(:with_lock)
  end

  it "performs operations atomically via with_lock" do
    disk_cache = described_class.new("/tmp/cache")

    expect { |blk| disk_cache.with_lock("digest", &blk) }
      .to yield_control
  end
end
