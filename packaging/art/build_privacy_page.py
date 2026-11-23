"""Builds packaging/StoreListing/privacy-policy.html from docs/PRIVACY.md.

The policy is shared by all of Jason Woodcock's apps, so one hosted copy serves every Store listing.

Run from the repository root:  python packaging/art/build_privacy_page.py
Handles the small Markdown subset the policy uses: headings, paragraphs, bullet lists,
bold, italics, inline code and links. Headings get ids so in-page links work.
Uses only the Python standard library.
"""
import html
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "docs", "PRIVACY.md")
TARGET = os.path.join(ROOT, "packaging", "StoreListing", "privacy-policy.html")

STYLE = """
  :root { color-scheme: light dark; --fg: #1f2937; --bg: #ffffff; --accent: #1e3a8a; --code: #f3f4f6; }
  @media (prefers-color-scheme: dark) { :root { --fg: #e5e7eb; --bg: #111827; --accent: #93c5fd; --code: #1f2937; } }
  body { margin: 0; background: var(--bg); color: var(--fg); font: 16px/1.6 "Segoe UI", system-ui, sans-serif; }
  main { max-width: 760px; margin: 0 auto; padding: 32px 16px 64px; }
  h1 { color: var(--accent); font-size: 2rem; margin-bottom: 0.25rem; }
  h2 { color: var(--accent); margin-top: 2rem; font-size: 1.35rem; }
  h3 { font-size: 1.1rem; margin-top: 1.5rem; }
  a { color: var(--accent); overflow-wrap: anywhere; }
  code { background: var(--code); padding: 1px 5px; border-radius: 4px; font-size: 0.9em; overflow-wrap: anywhere; }
"""


def inline(text):
    """Converts inline Markdown to HTML after escaping the text."""
    text = html.escape(text, quote=False)
    text = re.sub(r"`([^`]+)`", r"<code>\1</code>", text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"\*([^*]+)\*", r"<em>\1</em>", text)
    return text


def convert(lines):
    out, in_list = [], False
    for line in lines:
        if line.startswith("* "):
            if not in_list:
                out.append("<ul>")
                in_list = True
            out.append("  <li>" + inline(line[2:]) + "</li>")
            continue
        if in_list:
            out.append("</ul>")
            in_list = False
        if not line.strip():
            continue
        heading = re.match(r"(#+) (.*)", line)
        if heading:
            level = len(heading.group(1))
            anchor = re.sub(r"[^a-z0-9]+", "-", heading.group(2).lower()).strip("-")
            out.append(f'<h{level} id="{anchor}">{inline(heading.group(2))}</h{level}>')
        else:
            out.append("<p>" + inline(line) + "</p>")
    if in_list:
        out.append("</ul>")
    return "\n".join(out)


def main():
    with open(SOURCE, encoding="utf-8") as f:
        body = convert(f.read().splitlines())
    page = (
        '<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
        "<title>Privacy Policy | Jason Woodcock Apps</title>\n"
        f"<style>{STYLE}</style>\n</head>\n<body>\n<main>\n{body}\n</main>\n</body>\n</html>\n"
    )
    with open(TARGET, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)
    print("wrote", os.path.relpath(TARGET, ROOT))


if __name__ == "__main__":
    main()
