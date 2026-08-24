"""This module is the main entry point for the Music Flash Card Generator application.

It generates the ninja build files to compile the given LilyPond files into a
set of files including front PNG, back PNG, and audio file.
"""  # noqa: E501

import argparse
import os
import tempfile

from ninja_syntax import Writer


def _build_argparser():  # noqa: ANN202
    parser = argparse.ArgumentParser(
        description="Generate Ninja build files for Music Flash Card Generator."
    )
    parser.add_argument(
        "--input_dir",
        help="Input LilyPond directory containing .ly files.",
        required=True,
    )
    parser.add_argument(
        "--output_dir",
        help="Output directory for generated files.",
        required=True,
    )
    parser.add_argument(
        "--working_dir",
        default=tempfile.gettempdir(),
        help="Working directory for temporary files (default: system temp directory).",  # noqa: E501
    )
    return parser


def _process(ly_files, output_dir, working_dir):  # noqa: ANN001, ANN202
    this_directory = os.path.abspath(os.path.dirname(os.path.abspath(__file__)))  # noqa: PTH100, PTH120

    generate_aux_ly_script = os.path.join(  # noqa: PTH118
        this_directory, "generate_merged_ly_file.py"
    )

    sound_font_file = os.path.join(this_directory, "data", "SalC5Light2.sf2")  # noqa: PTH118

    template_front = os.path.join(this_directory, "data", "front.ly")  # noqa: PTH118
    template_back = os.path.join(this_directory, "data", "back.ly")  # noqa: PTH118

    ninja_file = os.path.join(working_dir, "build.ninja")  # noqa: PTH118
    with open(ninja_file, "w", encoding="utf-8") as f:  # noqa: PTH123
        writer = Writer(f)

        writer.rule(
            name="generate_merged_front_ly_file",
            command=f"python3 {generate_aux_ly_script} --user_file $in --template_file {template_front} --output_file $out",  # noqa: E501
            description="Generate front LilyPond file from $in",
        )
        writer.rule(
            name="generate_merged_back_ly_file",
            command=f"python3 {generate_aux_ly_script} --user_file $in --template_file {template_back} --output_file $out",  # noqa: E501
            description="Generate back LilyPond file from $in",
        )

        writer.rule(
            name="lilypond_generate_png",
            command="lilypond -dno-use-paper-size-for-page -dtall-page-formats=png -dbackend=eps -dno-gs-load-fonts -dinclude-eps-fonts -dresolution=1200 -o $out_without_ext --png $in",  # noqa: E501
            description="Generate PNG from LilyPond file $in",
        )

        writer.rule(
            name="convert_midi_to_wav",
            command=f"fluidsynth -a wasapi -g 1.0 -F $out {sound_font_file} $in",  # noqa: E501
            description="Convert MIDI file $in to WAV file $out",
        )

        writer.rule(
            name="convert_wav_to_mp3",
            command="ffmpeg -y -i $in -codec:a libmp3lame -qscale:a 2 $out",
            description="Convert WAV file $in to MP3 file $out",
        )

        writer.rule(
            name="cp",
            command="cp $in $out",
            description="Copy $in to $out",
        )

        for ly_file in ly_files:
            filename_without_ext = os.path.splitext(os.path.basename(ly_file))[  # noqa: PTH119, PTH122
                0
            ]

            front_ly = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}_front.ly")  # noqa: PTH118
            )
            front_png = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}.front.png")  # noqa: PTH118
            )
            front_png_without_ext = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}.front")  # noqa: PTH118
            )
            front_png_final = os.path.abspath(  # noqa: PTH100
                os.path.join(output_dir, f"{filename_without_ext}.front.png")  # noqa: PTH118
            )

            back_ly = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}_back.ly")  # noqa: PTH118
            )
            back_png = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}.back.png")  # noqa: PTH118
            )
            back_midi = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}.back.mid")  # noqa: PTH118
            )
            back_png_without_ext = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}.back")  # noqa: PTH118
            )
            back_png_final = os.path.abspath(  # noqa: PTH100
                os.path.join(output_dir, f"{filename_without_ext}.back.png")  # noqa: PTH118
            )

            file_wav = os.path.abspath(  # noqa: PTH100
                os.path.join(working_dir, f"{filename_without_ext}.wav")  # noqa: PTH118
            )
            file_mp3_final = os.path.abspath(  # noqa: PTH100
                os.path.join(output_dir, f"{filename_without_ext}.mp3")  # noqa: PTH118
            )

            writer.build(
                outputs=front_ly,
                rule="generate_merged_front_ly_file",
                inputs=os.path.abspath(ly_file),  # noqa: PTH100
            )
            writer.build(
                outputs=front_png,
                rule="lilypond_generate_png",
                inputs=os.path.abspath(front_ly),  # noqa: PTH100
                variables={"out_without_ext": front_png_without_ext},
            )
            writer.build(
                outputs=front_png_final,
                rule="cp",
                inputs=front_png,
            )

            writer.build(
                outputs=back_ly,
                rule="generate_merged_back_ly_file",
                inputs=os.path.abspath(ly_file),  # noqa: PTH100
            )
            writer.build(
                outputs=[back_png, back_midi],
                rule="lilypond_generate_png",
                inputs=os.path.abspath(back_ly),  # noqa: PTH100
                variables={"out_without_ext": back_png_without_ext},
            )
            writer.build(
                outputs=back_png_final,
                rule="cp",
                inputs=back_png,
            )

            writer.build(
                outputs=file_wav,
                rule="convert_midi_to_wav",
                inputs=os.path.abspath(back_midi),  # noqa: PTH100
            )
            writer.build(
                outputs=file_mp3_final,
                rule="cp",
                inputs=file_wav,
            )

        writer.build(
            outputs="all",
            rule="phony",
            inputs=[os.path.abspath(f) for f in ly_files],  # noqa: PTH100
        )


def main():  # noqa: ANN201, D103
    parser = _build_argparser()
    args = parser.parse_args()

    input_dir = args.input_dir
    output_dir = args.output_dir
    working_dir = args.working_dir

    os.makedirs(output_dir, exist_ok=True)  # noqa: PTH103
    os.makedirs(working_dir, exist_ok=True)  # noqa: PTH103

    ly_files = []
    for entry in os.listdir(input_dir):  # noqa: PTH208
        if entry.endswith(".ly"):
            ly_files.append(os.path.join(input_dir, entry))  # noqa: PTH118

    _process(ly_files, output_dir, working_dir)


if __name__ == "__main__":
    main()
