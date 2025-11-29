# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe Asciidoctor::Latexmath::Statistics::Collector do
  it "produces a stable log line" do
    collector = described_class.new
    collector.record_render(100)
    collector.record_render(300)
    collector.record_hit(4)
    collector.record_hit(6)

    expect(collector.to_line).to eq("latexmath stats: renders=2 cache_hits=2 avg_render_ms=200 avg_hit_ms=5")
  end

  it "returns nil when no activity tracked" do
    expect(described_class.new.to_line).to be_nil
  end

  it "rounds half-up for averages" do
    collector = described_class.new
    collector.record_render(101)
    collector.record_render(102)

    expect(collector.to_line).to include("avg_render_ms=102")
  end
end
