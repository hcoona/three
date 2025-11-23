# frozen_string_literal: true

require "asciidoctor"
require "asciidoctor/extensions"
require "asciidoctor-latexmath"

RSpec.describe "Processors contract" do
  before(:context) do
    processors_glob = File.expand_path("../../lib/asciidoctor/latexmath/processors/*.rb", __dir__)
    Dir.glob(processors_glob).sort.each do |processor_file|
      require processor_file
    end
  end

  let(:registry) { Asciidoctor::Extensions::Registry.new }

  context "when registered" do
    before do
      expect { Asciidoctor::Latexmath.register(registry) }
        .not_to raise_error
    end

    it "registers only a latexmath block processor" do
      block_processors = registry.instance_variable_get(:@block_processors)

      expect(block_processors.keys).to contain_exactly(:latexmath)
      expect(block_processors[:latexmath].size).to eq(1)
    end

    it "registers only a latexmath inline macro" do
      inline_macros = registry.instance_variable_get(:@inline_macros)

      expect(inline_macros.keys).to contain_exactly(:latexmath)
      expect(inline_macros[:latexmath].size).to eq(1)
    end

    it "does not register any block macros" do
      block_macros = registry.instance_variable_get(:@block_macros)
      expect(block_macros).to be_empty
    end

    it "does not register any tree processors" do
      tree_processors = registry.instance_variable_get(:@tree_processors)
      expect(tree_processors).to be_empty
    end

    it "does not register any tree processor extensions" do
      tree_processor_extensions = registry.instance_variable_get(:@tree_processor_extensions)
      expect(tree_processor_extensions).to be_empty
    end

    it "does not define a TreeProcessor constant" do
      processors_module = Asciidoctor::Latexmath::Processors
      expect(processors_module.const_defined?(:TreeProcessor, false)).to be(false)
    end

    it "does not define any TreeProcessor subclasses" do
      latexmath_classes = ObjectSpace.each_object(Class).select do |klass|
        name = klass.name
        name&.start_with?("Asciidoctor::Latexmath")
      end

      treeprocessor_subclasses = latexmath_classes.select do |klass|
        klass < ::Asciidoctor::Extensions::Treeprocessor
      end

      subclass_names = treeprocessor_subclasses.map(&:name).compact.sort

      expect(treeprocessor_subclasses).to be_empty, <<~MESSAGE
        expected no Asciidoctor::Latexmath classes to inherit from TreeProcessor, but found:
        #{subclass_names.join("\n")}
      MESSAGE
    end
  end
end
