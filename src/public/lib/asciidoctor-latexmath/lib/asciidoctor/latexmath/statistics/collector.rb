# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

module Asciidoctor
  module Latexmath
    module Statistics
      class Collector
        def initialize
          @render_durations = []
          @hit_durations = []
        end

        def record_render(duration_ms)
          @render_durations << duration_ms
        end

        def record_hit(duration_ms)
          @hit_durations << duration_ms
        end

        def to_line
          return nil if @render_durations.empty? && @hit_durations.empty?

          format(
            "latexmath stats: renders=%<renders>d cache_hits=%<hits>d avg_render_ms=%<avg_render>d avg_hit_ms=%<avg_hit>d",
            renders: @render_durations.size,
            hits: @hit_durations.size,
            avg_render: average(@render_durations),
            avg_hit: average(@hit_durations)
          )
        end

        private

        def average(values)
          return 0 if values.empty?

          sum = values.sum
          (sum.to_f / values.size).round(0, half: :up)
        end
      end
    end
  end
end
