import os

changelog_path = r"h:\pbl5\.local-agent-rules\CHANGELOG.md"

new_entry = """
## [2026-06-10] - Fix Speaker Notes Visibility and Canvas Clutter
- **Fixed Notes Canvas Clutter**: Filtered out elements with role `'notes'` from the active rendering slide canvas elements in `client/src/components/slide-studio/editorState.js`. This prevents speaker notes from rendering directly on the slide content area and keeps them private as requested.
- **Added Topbar Buttons**: Added "Thuyết trình" (Present) and "Ghi chú" (Notes) buttons to the main studio topbar (`folder-studio-topbar-modes` in `client/src/components/FolderStudio.js`). This makes the speaker notes panel toggling and presentation modes fully accessible in the workspace slide editor UI.
"""

if os.path.exists(changelog_path):
    with open(changelog_path, "a", encoding="utf-8") as f:
        f.write(new_entry)
    print("Successfully appended new entry to CHANGELOG.md")
else:
    with open(changelog_path, "w", encoding="utf-8") as f:
        f.write("# Changelog\n" + new_entry)
    print("Created CHANGELOG.md with new entry")
