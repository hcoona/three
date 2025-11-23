# frozen_string_literal: true

require "asciidoctor/extensions"
require "asciidoctor-latexmath"

RSpec.describe "Processors invariants" do
  it "registers exactly one block and one inline processor and nothing else" do
    registry = Asciidoctor::Extensions.create
    Asciidoctor::Latexmath.register(registry)

    block_processors = registry.instance_variable_get(:@block_processors)
    inline_macros = registry.instance_variable_get(:@inline_macros)
    tree_processors = registry.instance_variable_get(:@tree_processors)
    docinfo_processors = registry.instance_variable_get(:@docinfo_processors)
    postprocessors = registry.instance_variable_get(:@postprocessors)

    expect(block_processors.keys).to contain_exactly(:latexmath)
    expect(block_processors[:latexmath].size).to eq(1)

    expect(inline_macros.keys).to contain_exactly(:latexmath)
    expect(inline_macros[:latexmath].size).to eq(1)

    expect(tree_processors).to be_empty
    expect(docinfo_processors).to satisfy { |value| value.nil? || value.empty? }
    expect(postprocessors).to satisfy { |value| value.nil? || value.empty? }
  end

  it "does not load asciidoctor-mathematical or define Mathematical processors" do
    expect(Gem.loaded_specs.key?("asciidoctor-mathematical")).to be(false)
    expect(defined?(::Asciidoctor::Mathematical)).to be_nil

    stub_tool_availability(dvisvgm: true)

    source = <<~ADOC
      [latexmath]
      ++++
      x
      ++++

      latexmath:[y]
    ADOC

    convert_with_extension(source, attributes: {"imagesdir" => "images"})

    expect(Gem.loaded_specs.key?("asciidoctor-mathematical")).to be(false)
    expect(defined?(::Asciidoctor::Mathematical)).to be_nil
  end
end
