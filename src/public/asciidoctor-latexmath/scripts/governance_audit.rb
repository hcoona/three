#!/usr/bin/env ruby
# frozen_string_literal: true

require "optparse"
require "json"

options = {}
OptionParser.new do |parser|
  parser.banner = "Usage: governance_audit.rb --feature-dir PATH"
  parser.on("--feature-dir PATH", "Path to the feature specification directory") do |path|
    options[:feature_dir] = path
  end
end.parse!(ARGV)

feature_dir = options[:feature_dir] || ENV["FEATURE_DIR"]

unless feature_dir && Dir.exist?(feature_dir)
  warn "feature directory not found: #{feature_dir.inspect}"
  exit 1
end

spec_path = File.join(feature_dir, "spec.md")
tasks_path = File.join(feature_dir, "tasks.md")

unless File.file?(spec_path) && File.file?(tasks_path)
  warn "spec.md or tasks.md missing in #{feature_dir}"
  exit 1
end

spec_text = File.read(spec_path)
tasks_text = File.read(tasks_path)

fr_pattern = /FR-\d+/
requirement_ids = spec_text.scan(fr_pattern).uniq.sort
reference_ids = tasks_text.scan(fr_pattern).uniq.sort

missing = requirement_ids - reference_ids
unknown = reference_ids - requirement_ids

requirement_to_tasks = Hash.new { |hash, key| hash[key] = [] }
current_tasks = []

tasks_text.each_line do |line|
  task_matches = line.scan(/T\d{3}/)
  current_tasks = task_matches.empty? ? [] : task_matches

  fr_refs = line.scan(fr_pattern)
  next if fr_refs.empty? || current_tasks.empty?

  fr_refs.each do |fr|
    requirement_to_tasks[fr].concat(current_tasks)
  end
end

requirement_to_tasks.each_value do |tasks|
  next if tasks.empty?

  unique = tasks.uniq
  tasks.replace(unique)
end

covered_count = requirement_ids.length - missing.length
coverage = requirement_ids.empty? ? 1.0 : (covered_count.to_f / requirement_ids.length)

report = {
  "status" => (missing.empty? && unknown.empty?) ? "ok" : "warning",
  "feature_dir" => feature_dir,
  "total_requirements" => requirement_ids.length,
  "covered_requirements" => covered_count,
  "coverage" => coverage.round(3),
  "missing_requirements" => missing,
  "unknown_references" => unknown,
  "requirement_to_tasks" => requirement_to_tasks.sort.to_h
}

puts JSON.pretty_generate(report)
