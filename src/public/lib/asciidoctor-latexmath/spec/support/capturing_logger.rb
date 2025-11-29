# frozen_string_literal: true

class CapturingLogger
  attr_reader :messages

  def initialize
    @messages = []
  end

  def add(severity, message = nil, progname = nil)
    content = message
    content = yield if message.nil? && block_given?
    @messages << [severity, content]
  end

  def warn(message = nil, &block)
    add(:warn, message, &block)
  end

  def info(message = nil, &block)
    add(:info, message, &block)
  end

  def debug(message = nil, &block)
    add(:debug, message, &block)
  end

  def error(message = nil, &block)
    add(:error, message, &block)
  end
end
