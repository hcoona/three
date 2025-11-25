# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Atomic overwrite behavior" do
  it "overwrites existing targets atomically on cache misses" do
    stub_tool_availability(dvisvgm: true)

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath%nocache, target=images/eager.svg]
          ++++
          x
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        target_path = Dir.glob("images/**/*.svg").fetch(0, nil)
        expect(target_path).not_to be_nil
        original_content = File.read(target_path)
        original_inode = File.stat(target_path).ino

        File.write(target_path, "legacy content")
        manual_inode = File.stat(target_path).ino
        expect(manual_inode).to eq(original_inode)

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        final_content = File.read(target_path)
        final_inode = File.stat(target_path).ino

        expect(final_content).not_to eq("legacy content")
        expect(final_content).to eq(original_content)
        expect(final_inode).not_to eq(manual_inode)

        temp_files = Dir.glob("images/.eager.svg.tmp-*", File::FNM_DOTMATCH)
        expect(temp_files).to be_empty
      end
    end
  end
end
