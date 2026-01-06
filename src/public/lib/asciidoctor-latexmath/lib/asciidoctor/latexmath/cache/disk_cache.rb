# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "fileutils"
require "json"
require "digest"
require "time"

module Asciidoctor
  module Latexmath
    module Cache
      class DiskCache
        METADATA_FILENAME = "metadata.json"
        ARTIFACT_FILENAME = "artifact"
        METADATA_VERSION = 1

        def initialize(root)
          @root = root
          FileUtils.mkdir_p(@root)
        end

        def fetch(key_digest)
          entry_dir = entry_dir(key_digest)
          metadata_path = File.join(entry_dir, METADATA_FILENAME)
          artifact_path = File.join(entry_dir, ARTIFACT_FILENAME)
          return nil unless File.file?(metadata_path) && File.file?(artifact_path)

          metadata = JSON.parse(File.read(metadata_path))
          return nil unless valid_checksum?(artifact_path, metadata["checksum"])

          CacheEntry.new(
            final_path: artifact_path,
            format: to_symbol(metadata["format"]),
            content_hash: metadata["content_hash"],
            preamble_hash: metadata["preamble_hash"],
            fontsize: metadata["fontsize"],
            engine: metadata["engine"],
            ppi: metadata["ppi"],
            entry_type: to_symbol(metadata["entry_type"]),
            created_at: Time.parse(metadata["created_at"]),
            checksum: metadata["checksum"],
            size_bytes: metadata["size_bytes"],
            tool_presence: metadata["tool_presence"] || {}
          )
        rescue JSON::ParserError, Errno::ENOENT
          nil
        end

        def store(key_digest, cache_entry, source_path)
          entry_dir = entry_dir(key_digest)
          FileUtils.mkdir_p(entry_dir)
          artifact_path = File.join(entry_dir, ARTIFACT_FILENAME)
          metadata_path = File.join(entry_dir, METADATA_FILENAME)

          checksum = Digest::SHA256.file(source_path).hexdigest
          size_bytes = File.size(source_path)

          temp_artifact = prepare_temp_path(artifact_path)
          temp_metadata = prepare_temp_path(metadata_path)

          begin
            FileUtils.cp(source_path, temp_artifact)

            metadata = {
              "version" => METADATA_VERSION,
              "key" => key_digest,
              "format" => cache_entry.format.to_s,
              "content_hash" => cache_entry.content_hash,
              "preamble_hash" => cache_entry.preamble_hash,
              "fontsize" => cache_entry.fontsize,
              "engine" => cache_entry.engine,
              "ppi" => cache_entry.ppi,
              "entry_type" => cache_entry.entry_type.to_s,
              "created_at" => cache_entry.created_at.utc.iso8601,
              "checksum" => "sha256:#{checksum}",
              "size_bytes" => size_bytes,
              "tool_presence" => cache_entry.tool_presence
            }

            File.write(temp_metadata, JSON.pretty_generate(metadata))

            FileUtils.mv(temp_artifact, artifact_path)
            FileUtils.mv(temp_metadata, metadata_path)
          ensure
            FileUtils.rm_f(temp_artifact) if File.exist?(temp_artifact)
            FileUtils.rm_f(temp_metadata) if File.exist?(temp_metadata)
          end
        end

        def with_lock(key_digest)
          lock_path = File.join(root, "#{key_digest}.lock")
          FileUtils.mkdir_p(File.dirname(lock_path))

          attempts = 0
          base_sleep = 0.05

          loop do
            attempts += 1
            File.open(lock_path, File::RDWR | File::CREAT, 0o644) do |file|
              if file.flock(File::LOCK_EX | File::LOCK_NB)
                begin
                  return yield
                ensure
                  file.flock(File::LOCK_UN)
                end
              end
            end

            raise IOError, "could not obtain cache lock for #{key_digest}" if attempts >= 5

            sleep(base_sleep * (2**(attempts - 1)))
          end
        end

        private

        attr_reader :root

        def entry_dir(key_digest)
          File.join(root, key_digest)
        end

        def valid_checksum?(artifact_path, checksum_field)
          return false unless checksum_field

          algorithm, value = checksum_field.split(":", 2)
          return false unless algorithm == "sha256" && value

          Digest::SHA256.file(artifact_path).hexdigest == value
        rescue Errno::ENOENT
          false
        end

        def to_symbol(value)
          return nil if value.nil?

          value.to_s.strip.empty? ? nil : value.to_s.downcase.to_sym
        end

        def prepare_temp_path(path)
          dir = File.dirname(path)
          basename = File.basename(path)
          File.join(dir, ".#{basename}.tmp-#{Process.pid}-#{Thread.current.object_id}")
        end
      end
    end
  end
end
