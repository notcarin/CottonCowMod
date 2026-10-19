"""Converts the simple Markdown used in the release notes to Nexus Mods BBCode.

    python release/md_to_bbcode.py release/DEVLOG_1.0.2.md > release/DEVLOG_1.0.2_nexus.txt

Handles headings, **bold**, `code`, and "- " bullet lists, which is all the notes use.
"""
import re
import sys

HEADING_SIZES = {1: 6, 2: 5, 3: 4}


def inline(text):
    text = re.sub(r"\*\*(.+?)\*\*", r"[b]\1[/b]", text)
    text = re.sub(r"`(.+?)`", r"[font=Courier New]\1[/font]", text)
    return text


def convert(lines):
    out = []
    in_list = False
    for line in lines:
        line = line.rstrip("\n")
        bullet = re.match(r"^- (.*)", line)
        if bullet:
            if not in_list:
                out.append("[list]")
                in_list = True
            out.append("[*]" + inline(bullet.group(1)))
            continue
        if in_list:
            out.append("[/list]")
            in_list = False

        heading = re.match(r"^(#{1,3}) (.*)", line)
        if heading:
            size = HEADING_SIZES[len(heading.group(1))]
            out.append(f"[size={size}][b]{inline(heading.group(2))}[/b][/size]")
        else:
            out.append(inline(line))
    if in_list:
        out.append("[/list]")
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    with open(sys.argv[1], encoding="utf-8") as f:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(convert(f.readlines()))
