# Slide Export

## Muc tieu Phase 7

Phase 7 bien cac nut export trong Slide Studio thanh luong that: tai file HTML doc lap, mo ban print-friendly de dung Print / Save as PDF cua browser, va xuat PPTX co ban.

## Trang thai hien tai

- HTML export: done.
- Print / Save as PDF: done qua print-friendly HTML, khong phai PDF binary.
- PPTX basic: done bang OpenXML, khong them dependency moi.
- HTML preview cu van giu nguyen cho web preview.

## Format ho tro

- HTML file: tai ve file `.html` tu deck hien tai.
- Print / Save as PDF: mo HTML toi uu cho in, moi slide la mot page 16:9, browser xu ly Save as PDF.
- PPTX basic: file `.pptx` that, gom title slide va moi `SlideItem` thanh mot slide.

## Endpoint list

Preview cu:

```text
GET /api/slides/document/{documentId}/html
GET /api/slides/folders/{folderId}/html
```

Export Phase 7:

```text
GET /api/slides/{deckId}/export/html
GET /api/slides/{deckId}/export/print
GET /api/slides/{deckId}/export/pptx
```

## Security / ownership

Tat ca endpoint export nam trong `SlidesController`, co `[Authorize]`, va lay deck bang `deckId` truoc khi export. Controller kiem tra owner qua document hoac folder project cua deck; user khac khong duoc export deck khong thuoc ve minh.

## Frontend blob download flow

`client/src/services/api.js` dung Axios voi Bearer token:

- `slideService.exportDeckHtml(deckId)` goi `responseType: 'blob'`, doc filename tu `Content-Disposition`, tao object URL va trigger download.
- `slideService.exportDeckPptx(deckId)` lam tuong tu voi file `.pptx`.
- Print flow fetch `/export/print` thanh blob, mo object URL trong tab moi. Cach nay tranh loi 401 khi mo URL truc tiep vi tab moi khong tu gan Authorization header.

## Manual test checklist

1. Login.
2. Mo document hoac workspace da co slide deck.
3. Bam Download HTML / Tai HTML.
4. Mo file `.html`, kiem tra title, subtitle, slide heading, body va notes.
5. Bam Print / Save as PDF / In / Luu PDF.
6. Kiem tra tab print-friendly mo len va browser co the Save as PDF.
7. Bam Download PPTX / Tai PPTX.
8. Mo file `.pptx` bang PowerPoint hoac LibreOffice, kiem tra file khong corrupt.
9. Kiem tra khi chua co deck hoac deck dang generating thi nut export disabled hoac khong hien.
10. Neu co dieu kien, thu deck cua user khac va ky vong 403.

## Known limitations

- PPTX basic chua pixel-perfect so voi HTML preview.
- PDF la print-to-PDF cua browser, khong phai binary PDF do backend render.
- PPTX chua embed image candidate/local image.
- Speaker notes trong PPTX hien duoc render thanh text box nho tren slide, chua phai PowerPoint presenter notes pane.
- HTML/print export dung CSS inline toi thieu, khong nham thay the preview/editor day du.
