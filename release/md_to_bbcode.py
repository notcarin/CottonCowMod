"""Converts the simple Markdown used in the release notes to Nexus Mods BBCode.

    python release/md_to_bbcode.py release/DEVLOG_1.0.2.md > release/DEVLOG_1.0.2_nexus.txt

Handles headings, **bold**, `code`, [links](url), "- " and "1. " lists, and images on
their own line (![alt](url), centred), which is all the notes use.
"""
import re
import sys

HEADING_SIZES = {1: 6, 2: 5, 3: 4}


def inline(text):
    text = re.sub(r"\*\*(.+?)\*\*", r"[b]\1[/b]", text)
    text = re.sub(r"`(.+?)`", r"[font=Courier New]\1[/font]", text)
    text = re.sub(r"\[(.+?)\]\((\S+?)\)", r"[url=\2]\1[/url]", text)
    return text


def convert(lines):
    out = []
    open_list = None  # "[list]" or "[list=1]" while inside a list
    for line in lines:
        line = line.rstrip("\n")
        bullet = re.match(r"^- (.*)", line)
        numbered = re.match(r"^\d+\. (.*)", line)
        if bullet or numbered:
            kind = "[list]" if bullet else "[list=1]"
            if open_list != kind:
                if open_list:
                    out.append("[/list]")
                out.append(kind)
                open_list = kind
            out.append("[*]" + inline((bullet or numbered).group(1)))
            continue
        if open_list:
            out.append("[/list]")
            open_list = None

        image = re.match(r"^!\[.*?\]\((\S+?)\)$", line)
        heading = re.match(r"^(#{1,3}) (.*)", line)
        if image:
            out.append(f"[center][img]{image.group(1)}[/img][/center]")
        elif heading:
            size = HEADING_SIZES[len(heading.group(1))]
            out.append(f"[size={size}][b]{inline(heading.group(2))}[/b][/size]")
        else:
            out.append(inline(line))
    if open_list:
        out.append("[/list]")
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    with open(sys.argv[1], encoding="utf-8") as f:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(convert(f.readlines()))
