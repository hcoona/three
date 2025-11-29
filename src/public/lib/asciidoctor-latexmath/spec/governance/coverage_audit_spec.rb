# frozen_string_literal: true

require "json"
require "open3"

RSpec.describe "Governance coverage audit" do
  it "reports requirement coverage consistent with spec and tasks" do
    repo_root = File.expand_path("../..", __dir__)
    feature_dir = File.join(repo_root, "specs", "001-asciidoctor-latexmath-asciidoctor")

    spec_text = File.read(File.join(feature_dir, "spec.md"))
    tasks_text = File.read(File.join(feature_dir, "tasks.md"))

    fr_pattern = /FR-\d+/
    requirements = spec_text.scan(fr_pattern).uniq.sort
    references = tasks_text.scan(fr_pattern).uniq.sort
    missing = requirements - references
    unknown = references - requirements

    command = ["bundle", "exec", "ruby", File.join(repo_root, "scripts", "governance_audit.rb"), "--feature-dir", feature_dir]
    stdout, stderr, status = Open3.capture3(*command, chdir: repo_root)

    expect(status).to be_success, "governance audit failed: #{stderr}"

    report = JSON.parse(stdout)

    expect(report["total_requirements"]).to eq(requirements.length)
    expect(report["covered_requirements"]).to eq(requirements.length - missing.length)
    expect(report["missing_requirements"].sort).to eq(missing)
    expect(report["unknown_references"].sort).to eq(unknown)

    requirement_to_tasks = report.fetch("requirement_to_tasks", {})
    requirement_to_tasks.each do |fr, tasks|
      expect(fr).to match(fr_pattern)
      expect(tasks).to all(match(/T\d{3}/))
    end
  end
end
