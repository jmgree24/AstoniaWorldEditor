"""Build the distributable PDF from the maintained Markdown manual."""
from pathlib import Path
import re
import html
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, PageTemplate, Frame, Paragraph, Spacer, PageBreak,
    Table, TableStyle, Image, Preformatted, KeepTogether,
)
from reportlab.platypus.tableofcontents import TableOfContents
from PIL import Image as PILImage

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "UserManual.md"
OUTPUT = ROOT / "output" / "pdf" / "Astonia-World-Editor-Manual-0.3.2.pdf"
PAGE_W, PAGE_H = letter
MARGIN = 46
WIDTH = PAGE_W - MARGIN * 2
INK = colors.HexColor("#191921")
RED = colors.HexColor("#721F29")
GOLD = colors.HexColor("#B09659")
GRAY = colors.HexColor("#575B64")
PALE = colors.HexColor("#F2F3F5")

for name, file in [("Segoe", "segoeui.ttf"), ("Segoe-Bold", "segoeuib.ttf"),
                   ("Segoe-Italic", "segoeuii.ttf"), ("Consolas", "consola.ttf")]:
    pdfmetrics.registerFont(TTFont(name, str(Path("C:/Windows/Fonts") / file)))
pdfmetrics.registerFontFamily("Segoe", normal="Segoe", bold="Segoe-Bold", italic="Segoe-Italic", boldItalic="Segoe-Bold")

STYLES = {
    "body": ParagraphStyle("Body", fontName="Segoe", fontSize=10, leading=13,
        textColor=INK, spaceAfter=6, allowWidows=0, allowOrphans=0),
    "chapter": ParagraphStyle("Chapter", fontName="Segoe-Bold", fontSize=22, leading=27,
        textColor=INK, spaceAfter=16, keepWithNext=True),
    "section": ParagraphStyle("Section", fontName="Segoe-Bold", fontSize=12.5, leading=17,
        textColor=RED, spaceBefore=12, spaceAfter=7, keepWithNext=True),
    "list": ParagraphStyle("List", fontName="Segoe", fontSize=10, leading=13,
        textColor=INK, leftIndent=16, firstLineIndent=-13, spaceAfter=5,
        allowWidows=0, allowOrphans=0),
    "caption": ParagraphStyle("Caption", fontName="Segoe-Italic", fontSize=8.5, leading=11.5,
        textColor=GRAY, spaceBefore=5, spaceAfter=12, alignment=TA_CENTER),
    "cell": ParagraphStyle("Cell", fontName="Segoe", fontSize=9.0, leading=11.8,
        textColor=INK, spaceAfter=0, splitLongWords=True),
    "th": ParagraphStyle("TableHeader", fontName="Segoe-Bold", fontSize=9.0, leading=11.8,
        textColor=colors.white, spaceAfter=0),
    "code": ParagraphStyle("Code", fontName="Consolas", fontSize=8.4, leading=11.5,
        textColor=INK, leftIndent=12, rightIndent=10, spaceBefore=5, spaceAfter=12,
        backColor=PALE, borderPadding=10),
}


def inline(text):
    parts = re.split(r"(`[^`]+`|\*\*.*?\*\*)", text)
    result = []
    for part in parts:
        if part.startswith("`") and part.endswith("`"):
            result.append('<font name="Consolas" size="9">' + html.escape(part[1:-1]) + '</font>')
        elif part.startswith("**") and part.endswith("**"):
            result.append('<b>' + html.escape(part[2:-2]) + '</b>')
        else:
            result.append(html.escape(part))
    return "".join(result)


class ManualDoc(BaseDocTemplate):
    def __init__(self, path):
        super().__init__(str(path), pagesize=letter, leftMargin=MARGIN,
            rightMargin=MARGIN, topMargin=54, bottomMargin=48,
            title="Astonia World Editor - User Manual 0.3.2",
            author="Astonia World Editor", subject="Setup, editing tools, saving and PNG export",
            pageCompression=1)
        self.addPageTemplates(PageTemplate(id="Manual", frames=[Frame(MARGIN, 48, WIDTH, PAGE_H - 102, leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)], onPage=self.decorate))

    def decorate(self, canvas, doc):
        canvas.saveState()
        if doc.page == 1:
            canvas.setFillColor(INK)
            canvas.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
            canvas.setFillColor(GOLD)
            canvas.rect(MARGIN, PAGE_H - 48, 82, 4, fill=1, stroke=0)
            canvas.setFillColor(colors.HexColor("#45404A"))
            canvas.rect(PAGE_W - 115, 0, 115, PAGE_H, fill=1, stroke=0)
            canvas.setFillColor(RED)
            canvas.rect(PAGE_W - 115, 0, 7, PAGE_H, fill=1, stroke=0)
        else:
            canvas.setStrokeColor(GOLD)
            canvas.setLineWidth(1)
            canvas.line(MARGIN, PAGE_H - 37, PAGE_W - MARGIN, PAGE_H - 37)
            canvas.setFont("Segoe-Bold", 8)
            canvas.setFillColor(GRAY)
            canvas.drawString(MARGIN, PAGE_H - 28, "ASTONIA WORLD EDITOR")
            canvas.setFont("Segoe", 8)
            canvas.drawRightString(PAGE_W - MARGIN, PAGE_H - 28, "USER MANUAL  |  VERSION 0.3.2")
            canvas.setStrokeColor(colors.HexColor("#D7D8DD"))
            canvas.line(MARGIN, 35, PAGE_W - MARGIN, 35)
            canvas.setFont("Segoe", 8)
            canvas.drawString(MARGIN, 23, "Offline editing  |  October 14, 2026")
            canvas.drawRightString(PAGE_W - MARGIN, 23, str(doc.page))
        canvas.restoreState()

    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and flowable.style.name == "Chapter":
            label = flowable.getPlainText()
            key = "chapter-" + re.sub(r"[^a-z0-9]+", "-", label.lower()).strip("-")
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(label, key, level=0, closed=False)
            self.notify("TOCEntry", (0, label, self.page, key))


def cover():
    white = colors.HexColor("#EFEFE6")
    label = ParagraphStyle("CoverLabel", fontName="Segoe-Bold", fontSize=11, leading=16, textColor=colors.HexColor("#DCC89B"), spaceAfter=22)
    title = ParagraphStyle("CoverTitle", fontName="Segoe-Bold", fontSize=36, leading=41, textColor=white, spaceAfter=18)
    subtitle = ParagraphStyle("CoverSubtitle", fontName="Segoe", fontSize=17, leading=23, textColor=white, spaceAfter=28)
    body = ParagraphStyle("CoverBody", fontName="Segoe", fontSize=11, leading=17, textColor=white, spaceAfter=16, rightIndent=65)
    small = ParagraphStyle("CoverSmall", parent=body, fontSize=9, leading=14)
    return [Spacer(1, 45), Paragraph("STANDALONE MAP EDITING", label),
        Paragraph("ASTONIA<br/>WORLD EDITOR", title),
        Paragraph("User manual<br/>Version 0.3.2", subtitle),
        Paragraph("Install, connect your own resources,<br/>build maps and export clean images.", body),
        Spacer(1, 25), Paragraph("FIRST RUN", label),
        Paragraph("1. Extract the editor ZIP.<br/>2. Run AstoniaWorldEditor.exe.<br/>3. Choose Sources and select your res and zones folders.<br/>4. Open a map and save a working copy.", body),
        Spacer(1, 28), Paragraph("Windows x64 / .NET Framework 4.8<br/>No running client or server required.", small),
        Paragraph("October 14, 2026<br/>Screenshots use example game resources. Each recipient supplies their own compatible files.", small), PageBreak()]


def make_table(lines):
    parsed = [[cell.strip() for cell in row.strip().strip("|").split("|")] for row in lines]
    rows = [parsed[0]] + parsed[2:]
    count = len(rows[0])
    if count == 3:
        if rows[0][0] == "ID": widths = [30, 174, WIDTH - 204]
        elif rows[0][0] == "Tool": widths = [64, 32, WIDTH - 96]
        elif rows[0][0] == "Layer": widths = [90, 255, WIDTH - 345]
        else: widths = [95, 180, WIDTH - 275]
    else:
        widths = [164, WIDTH - 164]
        if rows[0][0] in ("Action", "Task"): widths = [WIDTH * 0.43, WIDTH * 0.57]
        if rows[0][0] == "Control": widths = [106, WIDTH - 106]
    cells = [[Paragraph(inline(cell), STYLES["th" if i == 0 else "cell"]) for cell in row] for i, row in enumerate(rows)]
    table = Table(cells, colWidths=widths, repeatRows=1, hAlign="LEFT", spaceBefore=4, spaceAfter=12,
        rowSplitRange=(4, len(cells) - 2) if len(cells) > 10 else None)
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), INK),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, PALE]),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 8), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
        ("LINEBELOW", (0, 0), (-1, 0), 1.2, GOLD),
        ("LINEBELOW", (0, 1), (-1, -1), 0.25, colors.HexColor("#D7D8DD")),
    ]))
    return table


def build():
    text = SOURCE.read_text(encoding="utf-8").split("<!-- pdf:contents -->", 1)[1]
    lines = text.splitlines()
    toc = TableOfContents()
    toc.levelStyles = [ParagraphStyle("ContentsItem", fontName="Segoe", fontSize=10.5, leading=16, leftIndent=8, firstLineIndent=-8, spaceBefore=6, textColor=INK)]
    story = cover() + [Paragraph("Contents", STYLES["section"]), Spacer(1, 10), toc, Spacer(1, 16),
        Paragraph("Click a chapter title to jump to it. PDF bookmarks provide the same chapter navigation.", STYLES["body"])]
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        if not line:
            i += 1; continue
        if line == "<!-- pdf:pagebreak -->":
            if not isinstance(story[-1], PageBreak): story.append(PageBreak())
            i += 1; continue
        if line.startswith("# "):
            story.append(Paragraph(inline(line[2:]), STYLES["chapter"])); i += 1; continue
        if line.startswith("## "):
            story.append(Paragraph(inline(line[3:]), STYLES["section"])); i += 1; continue
        if line.startswith("```"):
            code = []; i += 1
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code.append(lines[i]); i += 1
            story.append(Preformatted("\n".join(code), STYLES["code"])); i += 1; continue
        if line.startswith("|"):
            block = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                block.append(lines[i]); i += 1
            story.append(make_table(block)); continue
        match = re.match(r"!\[(.*?)\]\((.*?)\)", line)
        if match:
            path = SOURCE.parent / match.group(2)
            with PILImage.open(path) as bitmap: w, h = bitmap.size
            maximum_height = 220 if path.name == "flag-visibility.png" else 210 if path.name == "png-export-options.png" else 310
            scale = min(WIDTH / w, maximum_height / h)
            image = Image(str(path), width=w * scale, height=h * scale, hAlign="CENTER")
            i += 1
            while i < len(lines) and not lines[i].strip(): i += 1
            caption = match.group(1)
            if i < len(lines) and lines[i].strip().startswith("*") and not lines[i].strip().startswith("**"):
                caption = lines[i].strip().strip("*"); i += 1
            story.append(KeepTogether([Spacer(1, 5), image, Paragraph(inline(caption), STYLES["caption"])])); continue
        numbered = re.match(r"^(\d+)\. (.*)", line)
        if numbered:
            steps = []
            if isinstance(story[-1], Paragraph) and story[-1].style.name == "Section":
                steps.append(story.pop())
            while i < len(lines):
                step = re.match(r"^(\d+)\. (.*)", lines[i].strip())
                if not step: break
                steps.append(Paragraph('<font color="#721F29"><b>' + step.group(1) + '.</b></font> ' + inline(step.group(2)), STYLES["list"]))
                i += 1
            story.append(KeepTogether(steps)); continue
        if line.startswith("- "):
            prefix = numbered.group(1) + "." if numbered else "-"
            content = numbered.group(2) if numbered else line[2:]
            story.append(Paragraph('<font color="#721F29"><b>' + prefix + '</b></font> ' + inline(content), STYLES["list"])); i += 1; continue
        paragraph = [line]; i += 1
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#|\||```|<!--|!\[|- |\d+\. )", lines[i].strip()):
            paragraph.append(lines[i].strip()); i += 1
        story.append(Paragraph(inline(" ".join(paragraph)), STYLES["body"]))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    ManualDoc(OUTPUT).multiBuild(story)
    print(OUTPUT)


if __name__ == "__main__":
    build()
