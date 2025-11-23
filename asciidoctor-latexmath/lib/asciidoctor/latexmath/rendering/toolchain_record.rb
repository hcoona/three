# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

module Asciidoctor
  module Latexmath
    module Rendering
      class ToolchainRecord
        attr_reader :id, :available, :path

        def initialize(id:, available:, path: nil)
          @id = id
          @available = available
          @path = path
        end
      end
    end
  end
end
