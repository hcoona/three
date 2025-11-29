# frozen_string_literal: true

require "bundler/setup"
require "rspec"
require "aruba/rspec"
require "fileutils"
require "tmpdir"
require "pathname"
require "asciidoctor/latexmath/command_runner"

module SpecSupport
  module TmpDir
    def within_tmpdir(prefix: "latexmath-spec-")
      Dir.mktmpdir(prefix) do |dir|
        yield Pathname(dir)
      end
    end
  end
end

require_relative "support/document_helpers"
require_relative "support/tool_stub_helpers"
require_relative "support/capturing_logger"
require_relative "support/fake_command_runner"

RSpec.configure do |config|
  config.before(:suite) do
    Asciidoctor::Latexmath::CommandRunner.backend = SpecSupport::FakeCommandRunner.new
  end

  config.disable_monkey_patching!

  config.expect_with :rspec do |expectations|
    expectations.syntax = :expect
  end

  config.mock_with :rspec do |mocks|
    mocks.verify_partial_doubles = true
  end

  config.include SpecSupport::TmpDir
  config.include SpecSupport::DocumentHelpers
  config.include SpecSupport::ToolStubHelpers

  config.shared_context_metadata_behavior = :apply_to_host_groups

  Aruba.configure do |aruba|
    aruba.working_directory = "tmp/aruba"
    aruba.exit_timeout = ENV.fetch("ARUBA_TIMEOUT", 10).to_i
  end

  config.include Aruba::Api, type: :aruba
  config.before(:each, type: :aruba) do
    setup_aruba
  end

  config.after(:each, type: :aruba) do
    FileUtils.rm_rf(aruba.working_directory)
  end
end
