# frozen_string_literal: true

require "asciidoctor-latexmath"
require "find"

RSpec.describe "Cache hit directory enumeration" do
  it "does not enumerate cache directories on a pure cache hit" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-cachedir: cache

          [latexmath]
          ++++
          A
          ++++
        ADOC

        convert_with_extension(source)

        cachedir = File.expand_path("cache", Dir.pwd)
        expect(Dir.exist?(cachedir)).to be(true)

        guard = lambda do |method_name, args, kwargs|
          candidates = args.dup
          candidates.concat(kwargs.values) if kwargs && !kwargs.empty?

          candidates.any? do |argument|
            next false unless argument.is_a?(String)

            expanded = begin
              File.expand_path(argument, Dir.pwd)
            rescue
              argument
            end

            if expanded.start_with?(cachedir) || argument.include?(cachedir)
              call_stack = caller
              if call_stack.any? { |frame| frame.include?("tmpdir.rb") || frame.include?("fileutils.rb") }
                next false
              end

              raise "unexpected directory enumeration via #{method_name} with args=#{args.inspect} kwargs=#{kwargs.inspect}\n\n#{call_stack.join("\n")}"
            end

            false
          end
        end

        allow(Dir).to receive(:glob).and_wrap_original do |original, *args, **kwargs, &block|
          guard.call(:glob, args, kwargs)
          original.call(*args, **kwargs, &block)
        end

        allow(Dir).to receive(:children).and_wrap_original do |original, *args, **kwargs, &block|
          guard.call(:children, args, kwargs)
          original.call(*args, **kwargs, &block)
        end

        allow(Dir).to receive(:each_child).and_wrap_original do |original, *args, **kwargs, &block|
          guard.call(:each_child, args, kwargs)
          original.call(*args, **kwargs, &block)
        end

        allow(Dir).to receive(:foreach).and_wrap_original do |original, *args, **kwargs, &block|
          guard.call(:foreach, args, kwargs)
          original.call(*args, **kwargs, &block)
        end

        allow(Dir).to receive(:entries).and_wrap_original do |original, *args, **kwargs, &block|
          guard.call(:entries, args, kwargs)
          original.call(*args, **kwargs, &block)
        end

        allow(Find).to receive(:find).and_wrap_original do |original, *args, **kwargs, &block|
          guard.call(:find, args, kwargs)
          original.call(*args, **kwargs, &block)
        end

        expect do
          convert_with_extension(source)
        end.not_to raise_error
      end
    end
  end
end
