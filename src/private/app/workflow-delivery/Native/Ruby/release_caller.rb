# Concrete Ruby Release inputs and immutable ordinary-job transfers.
require 'json'
require 'fileutils'
require 'pathname'

module RubyReleaseCaller
  UNIT = 'hcoona-release-smoke-ruby'.freeze
  DECLARATION = 'tests/private/app/workflow-delivery/fixtures/products/' \
                'hcoona-release-smoke-ruby/workflow-delivery.release-unit.yml'.freeze

  module_function

  def read(path)
    JSON.parse(File.read(path, encoding: 'UTF-8'))
  end

  def write(path, value)
    File.write(path, JSON.pretty_generate(value) + "\n", encoding: 'UTF-8')
  end

  def subject
    destination = ENV.fetch('DESTINATION')
    raise 'Unsupported Ruby destination' unless %w[github-packages rubygems].include?(destination)

    {
      'revision' => ENV.fetch('GITHUB_SHA'),
      'selectedRef' => ENV.fetch('GITHUB_REF'),
      'unit' => UNIT,
      'declaration' => DECLARATION,
      'channel' => destination == 'github-packages' ? 'buddy' : 'official',
      'destination' => destination == 'github-packages' ? 'github-packages-ruby' : 'rubygems',
      'dryRun' => ENV.fetch('DRY_RUN') == 'true'
    }
  end

  def producer
    { 'subject' => subject, 'run' => ENV.fetch('GITHUB_RUN_ID'),
      'attempt' => ENV.fetch('GITHUB_RUN_ATTEMPT') }
  end

  def main(args)
    root = Pathname.new(ENV.fetch('RUNNER_TEMP')).realpath
    control = root.join('control')
    prepared = root.join('prepared')
    case args
    when ['request']
      raise 'Unsupported Ruby repository' unless ENV.fetch('GITHUB_REPOSITORY') == 'hcoona/three'
      raise 'Live rerun is unavailable' if !subject['dryRun'] &&
                                          ENV.fetch('GITHUB_RUN_ATTEMPT') != '1'

      prepared.mkdir
      scratch = root.join('preparation-scratch')
      scratch.mkdir
      write(control.join('producer.json'), producer)
      write(prepared.join('producer.json'), producer)
      write(prepared.join('request.json'), {
        'subject' => subject, 'checkout' => ENV.fetch('GITHUB_WORKSPACE'),
        'scratch' => scratch.to_s,
        'tools' => { 'ruby' => ENV.fetch('RUBY_EXECUTABLE'),
                     'gem' => ENV.fetch('GEM_EXECUTABLE'),
                     'dotnet' => ENV.fetch('DOTNET_EXECUTABLE') }
      })
    when ['originals']
      preparation = read(prepared.join('preparation.json'))
      raise 'Preparation subject mismatch' unless (
        preparation.fetch('prepared').fetch('subject') == subject
      )

      scratch = root.join('preparation-scratch').realpath
      preparation.fetch('prepared').fetch('files').each do |original|
        relative = Pathname.new(original.fetch('path'))
        source = scratch.join(relative).realpath
        raise 'Original transfer leaves preparation' if relative.absolute? ||
                                                       !source.to_s.start_with?(scratch.to_s + '/')

        target = prepared.join(relative)
        FileUtils.mkdir_p(target.dirname)
        FileUtils.copy_file(source, target)
      end
    when ['state']
      observation = read(root.join('observation/observation.json')).fetch('observation')
      raise 'Observation subject mismatch' unless observation.fetch('subject') == subject

      needed = observation.fetch('files').any? { |file| file.fetch('state') == 'Absent' }
      File.open(ENV.fetch('GITHUB_OUTPUT'), 'a', encoding: 'UTF-8') do |output|
        output.puts("write-needed=#{needed}")
      end
    else
      case args.first
      when 'validate'
        raise 'Missing transfer kinds' if args.length < 2

        args.drop(1).each do |kind|
          raise 'Transferred producer mismatch' unless (
            read(root.join(kind, 'producer.json')) == producer
          )
        end
        raise 'Transferred Ruby subject mismatch' unless
          read(prepared.join('preparation.json')).fetch('prepared').fetch('subject') == subject
      when 'phase'
        raise 'Unsupported phase input' unless args.length == 2

        phase = root.join(args[1])
        phase.mkdir
        write(phase.join('producer.json'), producer)
      when 'source-config', 'publisher-config', 'consumer-config'
        raise 'Unsupported tools input' unless args.length == 2

        scratch = root.join(args[1])
        scratch.mkdir
        tools = { 'ruby' => ENV.fetch('RUBY_EXECUTABLE'), 'scratch' => scratch.to_s }
        case args.first
        when 'source-config'
          tools['helper'] = control.join('source.rb').to_s
        when 'publisher-config'
          tools['gem'] = ENV.fetch('GEM_EXECUTABLE')
        when 'consumer-config'
          tools.merge!('factsHelper' => control.join('facts.rb').to_s,
                       'sourceHelper' => control.join('source.rb').to_s,
                       'consumerHelper' => control.join('consumer.rb').to_s)
        end
        write(root.join(args.first.sub('-config', '-tools') + '.json'), tools)
      else
        raise 'Unsupported ordinary Ruby caller operation'
      end
    end
  end
end

RubyReleaseCaller.main(ARGV) if $PROGRAM_NAME == __FILE__
