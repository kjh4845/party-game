#!/usr/bin/env ruby

require "rexml/document"
require "set"

if ARGV.empty?
  warn "usage: ruby tools/verify_nunit_result.rb <result.xml> [required-full-test-name ...]"
  exit 2
end

path = ARGV.shift
document = REXML::Document.new(File.read(path))
root = document.root
abort "missing test-run root" unless root&.name == "test-run"

counter_names = %w[testcasecount total passed failed skipped inconclusive]
counters = counter_names.to_h do |name|
  value = root.attributes[name]
  abort "missing or nonnumeric #{name}" unless value&.match?(/\A\d+\z/)
  [name, Integer(value, 10)]
end

testcase_count = counters.fetch("testcasecount")
total = counters.fetch("total")
passed = counters.fetch("passed")
failed = counters.fetch("failed")
skipped = counters.fetch("skipped")
inconclusive = counters.fetch("inconclusive")
not_run_attribute = root.attributes["not-run"]
if not_run_attribute
  abort "nonnumeric not-run" unless not_run_attribute.match?(/\A\d+\z/)
  not_run = Integer(not_run_attribute, 10)
else
  not_run = testcase_count - total
end
abort "zero tests discovered" unless total.positive?
abort "invalid testcasecount/total" unless testcase_count >= total
abort "not-run count mismatch" unless not_run == testcase_count - total
abort "test run did not pass" unless root.attributes["result"] == "Passed"
abort "failed/skipped/inconclusive/not-run tests present" unless
  failed.zero? && skipped.zero? && inconclusive.zero? && not_run.zero?
abort "passed count mismatch" unless passed == total

cases = []
REXML::XPath.each(document, "//test-case") { |node| cases << node }
abort "test-case count mismatch" unless cases.length == total
abort "non-passing test-case present" unless cases.all? { |node| node.attributes["result"] == "Passed" }

actual_names = cases.map { |node| node.attributes["fullname"] }.to_set
missing = ARGV.reject { |required| actual_names.include?(required) }
abort "missing required tests: #{missing.join(', ')}" unless missing.empty?

puts "NUNIT_RESULT_VALIDATION=PASS total=#{total} required=#{ARGV.length}"
