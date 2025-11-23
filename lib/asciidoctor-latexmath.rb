# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "asciidoctor"
require "asciidoctor/extensions"
require_relative "asciidoctor/latexmath/version"
require_relative "asciidoctor/latexmath/processors/block_processor"
require_relative "asciidoctor/latexmath/processors/inline_macro_processor"
require_relative "asciidoctor/latexmath/processors/statistics_postprocessor"
require_relative "asciidoctor/latexmath/converters/html5"

module Asciidoctor
  module Latexmath
    class << self
      def register(registry = ::Asciidoctor::Extensions)
        block_extension = registry.block Processors::BlockProcessor
        inline_extension = registry.inline_macro Processors::InlineMacroProcessor
        ensure_processor_collection!(registry, :@block_processors, :latexmath, block_extension)
        ensure_processor_collection!(registry, :@inline_macros, :latexmath, inline_extension)
        registry.postprocessor Processors::StatisticsPostprocessor

        ensure_aliases!(registry)
        ensure_empty_collection!(registry, :@block_macros, {})
        ensure_empty_collection!(registry, :@tree_processors, [])
        ensure_empty_collection!(registry, :@tree_processor_extensions, [])

        registry
      end

      def reset_render_counters!
        @render_invocations = 0
      end

      def record_render_invocation!
        @render_invocations = render_invocations + 1
      end

      def render_invocations
        @render_invocations ||= 0
      end

      private

      def ensure_processor_collection!(registry, ivar, canonical_name, extension)
        collection = registry.instance_variable_get(ivar)
        unless collection
          collection = {}
          registry.instance_variable_set(ivar, collection)
        end

        canonical_name = canonical_name.to_sym
        collection[canonical_name] ||= []
        unless collection[canonical_name].include?(extension)
          collection[canonical_name] << extension
        end

        collection
      end

      def ensure_aliases!(registry)
        ensure_collection_alias!(registry.instance_variable_get(:@block_extensions), :latexmath, :stem)
        ensure_collection_alias!(registry.instance_variable_get(:@inline_macro_extensions), :latexmath, :stem)
        ensure_collection_alias!(registry.instance_variable_get(:@block_processors), :latexmath, :stem)
        ensure_collection_alias!(registry.instance_variable_get(:@inline_macros), :latexmath, :stem)
      end

      def ensure_collection_alias!(collection, canonical_name, alias_name)
        return unless collection
        canonical_name = canonical_name.to_sym
        alias_name = alias_name&.to_sym
        return unless alias_name

        existing_default = collection.default_proc

        collection.default_proc = lambda do |hash, key|
          key_sym = key.to_sym
          if key_sym == alias_name
            hash[canonical_name]
          elsif existing_default
            existing_default.call(hash, key)
          end
        end
      end

      def ensure_empty_collection!(registry, ivar, empty_value)
        value = registry.instance_variable_get(ivar)
        if value.nil?
          registry.instance_variable_set(ivar, empty_value.dup)
        else
          value
        end
      end
    end
  end
end

Asciidoctor::Extensions.register do
  Asciidoctor::Latexmath.register(self)
end
