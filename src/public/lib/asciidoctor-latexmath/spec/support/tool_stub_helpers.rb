# frozen_string_literal: true

require "asciidoctor-latexmath"

module SpecSupport
  module ToolStubHelpers
    def stub_tool_availability(dvisvgm: true, pdf2svg: false, pdftoppm: true, magick: false, gs: false)
      Asciidoctor::Latexmath::Rendering::ToolDetector.reset!
      allow(Asciidoctor::Latexmath::Rendering::ToolDetector).to receive(:lookup).and_wrap_original do |original, identifier, command, &block|
        case identifier
        when :dvisvgm
          tool_record(:dvisvgm, dvisvgm, command)
        when :pdf2svg
          tool_record(:pdf2svg, pdf2svg, command)
        when :pdftoppm
          tool_record(:pdftoppm, pdftoppm, command)
        when :magick
          tool_record(:magick, magick, command)
        when :gs
          tool_record(:gs, gs, command)
        else
          original.call(identifier, command, &block)
        end
      end
    end

    private

    def tool_record(id, available, command)
      path = available ? "/usr/bin/#{id}" : command
      Asciidoctor::Latexmath::Rendering::ToolchainRecord.new(id: id, available: available, path: path)
    end
  end
end
