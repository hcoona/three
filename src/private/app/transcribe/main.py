import asyncio
import io
import logging
import math
import os
from typing import cast

import dotenv
from azure.core.credentials_async import AsyncTokenCredential
from azure.identity.aio import AzureCliCredential, get_bearer_token_provider
from openai import AsyncAzureOpenAI, AsyncOpenAI
from pydub import AudioSegment


def build_arg_parser():
    import argparse

    parser = argparse.ArgumentParser(
        description="Transcribe audio using OpenAI API."
    )
    parser.add_argument(
        "file",
        type=str,
        help="Path to the audio file to be transcribed.",
    )
    return parser


async def main() -> None:
    parser = build_arg_parser()
    args = parser.parse_args()

    api_base = os.environ.get("OPENAI_BASE_URL")
    if not api_base:
        raise ValueError("OPENAI_BASE_URL environment variable is not set.")

    api_version = os.environ.get("AZURE_OPENAI_API_VERSION")
    if api_version:
        token_provider = get_bearer_token_provider(
            cast(AsyncTokenCredential, AzureCliCredential()),
            "https://cognitiveservices.azure.com/.default",
        )
        client = AsyncAzureOpenAI(
            azure_endpoint=api_base,
            api_version=api_version,
            azure_ad_token_provider=token_provider,
        )
    else:
        api_key = os.environ.get("OPENAI_API_KEY")
        if not api_key:
            raise ValueError("OPENAI_API_KEY environment variable is not set.")

        client = AsyncOpenAI(
            base_url=api_base,
            api_key=api_key,
        )

    audio = AudioSegment.from_file(args.file)
    if not isinstance(audio, AudioSegment):
        raise TypeError("Expected decoded audio to be an AudioSegment.")
    total_duration_seconds = math.ceil(len(audio) / 1000.0)

    segment_duration_seconds = 180  # 3 minutes
    segment_overlap_seconds = 30  # 30 seconds overlap

    start = 0
    index = 1

    _TEMP_AUDIO_FILE_FORMAT = "mp3"
    with io.BytesIO() as buffer:
        buffer.name = f"temp_audio.{_TEMP_AUDIO_FILE_FORMAT}"
        while start < total_duration_seconds:
            end = min(start + segment_duration_seconds, total_duration_seconds)
            if start == end:
                break

            segment = audio[start * 1000 : end * 1000]
            if not isinstance(segment, AudioSegment):
                raise TypeError("Expected sliced audio to be an AudioSegment.")
            start += segment_duration_seconds - segment_overlap_seconds
            index += 1

            buffer.seek(0)
            buffer.truncate()
            segment.export(buffer, format=_TEMP_AUDIO_FILE_FORMAT)
            buffer.seek(0)
            response = await client.audio.transcriptions.create(
                file=buffer,
                model="gpt-4o-transcribe",
                response_format="text",
            )
            print(response)

        buffer.close()

    # TODO(shuaizhang): Merge up all segments into a single transcript.
    # PROMPT = (
    #     "这些是带有 overlap 的 transcription 片段。这些 transcription 不带有时间戳。"
    #     "不需要识别讲话人。不需要生成合并后的 transcription。根据合并后的内容进行详细回顾。"
    # )


if __name__ == "__main__":
    dotenv.load_dotenv()
    logging.basicConfig(level=logging.INFO)
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("azure.identity").setLevel(logging.WARNING)

    asyncio.run(main())
