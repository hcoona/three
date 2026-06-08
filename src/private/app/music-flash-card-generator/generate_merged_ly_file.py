"""This utility generates a merged LilyPond file that includes both the user file and the front/back includes."""  # noqa: E501

import argparse
import os


def _build_argparser():  # noqa: ANN202
    parser = argparse.ArgumentParser(
        description="Generate a merged LilyPond file that includes both the user file and the front/back includes."  # noqa: E501
    )
    parser.add_argument(
        "--user_file",
        help="Path to the user-defined LilyPond file.",
        required=True,
    )
    parser.add_argument(
        "--template_file",
        help="Path to the template LilyPond file to include.",
        required=True,
    )
    parser.add_argument(
        "--output_file",
        help="Path to the output merged LilyPond file.",
        required=True,
    )
    return parser


def main() -> None:  # noqa: D103
    parser = _build_argparser()
    args = parser.parse_args()
    with open(args.output_file, "w", encoding="utf-8") as output_file:  # noqa: PTH123
        output_file.write('\\version "2.24.3"\n')
        output_file.write(f'\\include "{os.path.abspath(args.user_file)}"\n')  # noqa: PTH100
        output_file.write(
            f'\\include "{os.path.abspath(args.template_file)}"\n'  # noqa: PTH100
        )


if __name__ == "__main__":
    main()
