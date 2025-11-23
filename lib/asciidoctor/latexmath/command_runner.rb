# frozen_string_literal: true

require_relative "errors"

module Asciidoctor
  module Latexmath
    class CommandRunner
      Result = Struct.new(:stdout, :stderr, :exit_status, :duration, keyword_init: true)

      class << self
        attr_writer :backend

        def backend
          @backend ||= SystemRunner.new
        end

        def run(command, timeout:, chdir:, env: {}, stdin: nil)
          backend.run(command, timeout: timeout, chdir: chdir, env: env, stdin: stdin)
        end

        def with_backend(temp_backend)
          previous = backend
          self.backend = temp_backend
          yield
        ensure
          self.backend = previous
        end
      end

      class NullRunner
        def run(_command, timeout:, **_rest)
          raise ArgumentError, "timeout must be positive" unless timeout&.positive?

          Result.new(stdout: "", stderr: "", exit_status: 0, duration: 0.0)
        end
      end

      class SystemRunner
        DEFAULT_WAIT_INTERVAL = 0.05
        KILL_GRACE_SECONDS = 2.0

        def run(command, timeout:, chdir:, env: {}, stdin: nil)
          raise ArgumentError, "timeout must be positive" unless timeout&.positive?

          start_time = Process.clock_gettime(Process::CLOCK_MONOTONIC)
          stdout_read, stdout_write = IO.pipe
          stderr_read, stderr_write = IO.pipe

          spawn_options = {
            chdir: chdir,
            out: stdout_write,
            err: stderr_write,
            pgroup: true
          }

          pid = Process.spawn(env, *command, spawn_options)
          stdout_write.close
          stderr_write.close

          stdout_thread = Thread.new { stdout_read.read }
          stderr_thread = Thread.new { stderr_read.read }

          wait_with_timeout(pid, timeout)

          exit_status = $?.exitstatus
          stdout_output = stdout_thread.value
          stderr_output = stderr_thread.value
          duration = Process.clock_gettime(Process::CLOCK_MONOTONIC) - start_time

          Result.new(stdout: stdout_output, stderr: stderr_output, exit_status: exit_status, duration: duration)
        rescue Errno::ENOENT => error
          raise StageFailureError, "Executable not found: #{error.message.split.last}"
        ensure
          stdout_read&.close unless stdout_read&.closed?
          stderr_read&.close unless stderr_read&.closed?
        end

        private

        def wait_with_timeout(pid, timeout)
          deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + timeout

          loop do
            result = Process.wait(pid, Process::WNOHANG)
            return result if result

            if Process.clock_gettime(Process::CLOCK_MONOTONIC) >= deadline
              terminate_process_group(pid)
              raise RenderTimeoutError, "command timed out after #{timeout}s"
            end

            sleep(DEFAULT_WAIT_INTERVAL)
          end
        end

        def terminate_process_group(pid)
          Process.kill("TERM", -pid)
          grace_deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + KILL_GRACE_SECONDS

          loop do
            result = Process.wait(pid, Process::WNOHANG)
            return result if result

            break if Process.clock_gettime(Process::CLOCK_MONOTONIC) >= grace_deadline

            sleep(DEFAULT_WAIT_INTERVAL)
          end

          Process.kill("KILL", -pid)
          Process.wait(pid)
        rescue Errno::ESRCH, Errno::ECHILD
        end
      end
    end
  end
end
