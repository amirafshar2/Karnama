"""Build docs/PROJECT_MAP.html from docs/PROJECT_MAP.md (offline: mermaid + marked from tools/vendor)."""
import json
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "PROJECT_MAP.md"
TEMPLATE = ROOT / "tools" / "map_template.html"
OUT = ROOT / "docs" / "PROJECT_MAP.html"


def main() -> None:
    markdown = SRC.read_text(encoding="utf-8")
    html = TEMPLATE.read_text(encoding="utf-8")
    # Escape "</" so the markdown can never close the <script> tag it is embedded in.
    md_json = json.dumps(markdown, ensure_ascii=False).replace("</", "<\\/")
    html = html.replace("__MARKDOWN_JSON__", md_json).replace(
        "__BUILT_AT__", datetime.now().strftime("%Y-%m-%d %H:%M")
    )
    OUT.write_text(html, encoding="utf-8")
    print(f"OK -> {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
