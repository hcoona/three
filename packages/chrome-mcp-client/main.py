import asyncio
import json

import typer
from fastmcp import Client
from mcp.types import TextContent

app = typer.Typer(no_args_is_help=True, help="Chrome MCP Client CLI")

# client = Client("https://learn.microsoft.com/api/mcp")
client = Client("http://127.0.0.1:12306/mcp")


@app.command()
def list_tools() -> None:
    """List all available tools in the Chrome MCP server."""
    asyncio.run(list_tools_async())


async def list_tools_async() -> None:
    """List all available tools in the Chrome MCP server."""
    async with client:
        await client.ping()

        typer.echo("Listing all tools...")
        tools = await client.list_tools()
        for tool in tools:
            typer.echo(
                f"Name: {tool.name}, Description: {tool.description}\n\tSchema: {tool.inputSchema}"
            )
        typer.echo("Tools listed successfully.")

        result = await client.call_tool("get_windows_and_tabs")
        assert result.content, "Expected content in the result"
        assert len(result.content) == 1, (
            "Expected exactly one content item in the result"
        )
        assert hasattr(result.content[0], "text"), (
            "Expected content item to have 'text' attribute"
        )
        assert isinstance(result.content[0], TextContent), (
            "Expected content item to be of type TextContent"
        )
        result_object = json.loads(result.content[0].text)
        result_inner_data_object = json.loads(
            result_object["data"]["content"][0]["text"]
        )
        typer.echo(
            f"Result of 'get_windows_and_tabs': {json.dumps(result_inner_data_object, indent=2, ensure_ascii=False)}"
        )


if __name__ == "__main__":
    app()
