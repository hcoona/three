# frozen_string_literal: true

require "cgi"

module Asciidoctor
  module Latexmath
    module HtmlBuilder
      LINE_FEED = "\n"

      module_function

      def block_html(node, result)
        attrs = result.attributes.dup
        target = attrs.delete("target") || result.target
        alt_text = attrs.delete("alt") || result.alt_text || ""
        original = attrs.delete("data-latex-original") || result.alt_text || ""
        role_attr = attrs.delete("role") || "math"
        width = attrs.delete("width")
        height = attrs.delete("height")

        residual_attrs = attrs.except("format")

        classes = ["imageblock"]
        classes.concat(node.roles) if node.respond_to?(:roles)
        classes << "math" unless classes.include?("math")
        class_value = classes.reject(&:empty?).uniq.join(" ")

        div_attrs = []
        div_attrs << %(id="#{escape_html_attribute(node.id)}") if node.id
        div_attrs << %(class="#{escape_html_attribute(class_value)}") unless class_value.empty?
        div_attrs << %(role="#{escape_html_attribute(role_attr)}") if role_attr
        div_attrs << %(data-latex-original="#{escape_html_attribute(original)}") unless original.to_s.empty?
        if node.respond_to?(:attr) && (align = node.attr("align"))
          div_attrs << %(style="text-align: #{escape_html_attribute(align)};")
        end

        img_attrs = []
        img_attrs << %(src="#{escape_html_attribute(target)}") if target
        img_attrs << %(alt="#{escape_html_attribute(alt_text)}") unless alt_text.to_s.empty?
        img_attrs << %(role="#{escape_html_attribute(role_attr)}") if role_attr
        img_attrs << %(data-latex-original="#{escape_html_attribute(original)}") unless original.to_s.empty?
        img_attrs << %(width="#{escape_html_attribute(width)}") if width
        img_attrs << %(height="#{escape_html_attribute(height)}") if height
        residual_attrs.each do |key, value|
          next if value.nil?

          img_attrs << %(#{key}="#{escape_html_attribute(value)}")
        end

        div_attr_string = div_attrs.empty? ? "" : " " + div_attrs.join(" ")
        lines = []
        lines << %(<div#{div_attr_string}>)
        if node.respond_to?(:title?) && node.title?
          caption = node.respond_to?(:captioned_title) ? node.captioned_title : node.title
          lines << %(<div class="title">#{caption}</div>)
        end
        lines << %(<div class="content">)
        lines << %(<img #{img_attrs.join(" ")}>)
        lines << %(</div>)
        lines << %(</div>)
        lines.join(LINE_FEED)
      end

      def inline_html(node, result)
        attrs = result.attributes.dup
        target = attrs.delete("target") || result.target
        alt_text = attrs.delete("alt") || result.alt_text || ""
        role_attr = attrs.delete("role") || "math"
        original = attrs.delete("data-latex-original") || result.alt_text || ""
        width = attrs.delete("width")
        height = attrs.delete("height")

        classes = ["image"]
        classes.concat(role_attr.to_s.split) if role_attr
        if node.respond_to?(:role) && node.role
          classes.concat(node.role.to_s.split)
        end
        classes << "math" unless classes.include?("math")
        span_class = classes.reject(&:empty?).uniq.join(" ")

        img_attrs = []
        img_attrs << %(src="#{escape_html_attribute(target)}") if target
        img_attrs << %(alt="#{escape_html_attribute(alt_text)}") unless alt_text.to_s.empty?
        img_attrs << %(role="#{escape_html_attribute(role_attr)}") if role_attr
        img_attrs << %(data-latex-original="#{escape_html_attribute(original)}") unless original.to_s.empty?
        img_attrs << %(width="#{escape_html_attribute(width)}") if width
        img_attrs << %(height="#{escape_html_attribute(height)}") if height
        attrs.each do |key, value|
          next if key == "format" || value.nil?

          img_attrs << %(#{key}="#{escape_html_attribute(value)}")
        end

        span_attr = span_class.empty? ? "" : %( class="#{escape_html_attribute(span_class)}")
        %(<span#{span_attr}><img #{img_attrs.join(" ")}></span>)
      end

      def escape_html_attribute(value)
        ::CGI.escapeHTML(value.to_s)
      end
    end
  end
end
