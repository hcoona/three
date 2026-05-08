# frozen_string_literal: true

require_relative "rubygems/version"

module Hcoona
  module ReleaseSmoke
    module Rubygems
      def self.smoke_message
        "hcoona-release-smoke-rubygems"
      end
    end
  end
end
