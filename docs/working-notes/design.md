# Design Spec: Full Slide System

## 1. Mục tiêu

Tài liệu này chốt hướng UI/UX để duyệt trước khi code cho full slide system của sản phẩm. Phạm vi bao gồm:

- Màn hình `/documents`, hiện đang dùng `client/src/components/DocumentList.js`
- Màn hình `/slides/:documentId`, hiện đang dùng `client/src/components/SlideStudio.js`
- Handoff giữa hai màn hình để người dùng đi từ tài liệu sang deck và sang chỉnh sửa chi tiết mà không mất ngữ cảnh

Mục tiêu sản phẩm:

- Đi từ document sang slide deck có ảnh với số bước ít nhất
- Cho người dùng thấy tiến trình AI rõ ràng: outline, slide content, image sourcing
- Giữ ranh giới bảo mật: local AI suy luận nội dung, internet worker chỉ xử lý mô tả ảnh đã redacted
- Dễ duyệt, dễ sửa, có đường lui về text-only nếu ảnh không ổn

Nguyên tắc thiết kế:

- Mỗi card chỉ nên truyền tải một ý chính
- Ảnh là lớp bổ trợ cho thông điệp, không lấn át nội dung học thuật
- Luôn phân biệt rõ "đang tạo nội dung" và "đang tìm ảnh"
- Mọi trạng thái đều phải có lối thoát: thử lại, đổi ảnh, bỏ ảnh, mở studio
- UI phải hoạt động tốt cả khi deck mới chỉ có outline, chưa có slide hoàn chỉnh

## 2. Bối cảnh hiện tại trong repo

### Routes đang chạy

- `client/src/App.js` route `/documents` trỏ tới `DocumentList`
- `client/src/App.js` route `/slides/:documentId` trỏ tới `SlideStudio`

### Hình dạng UI hiện tại

- `DocumentList.js` đã có document cards, progress panel, inline slide preview, nút mở Studio, nút HTML/PDF
- `SlideStudio.js` đã có tinh thần Gamma-style: brief panel, live outline, preview canvas, theme cards, edit nội dung từng slide
- `App.css` đã có hai cụm style rõ ràng:
  - `.slide-inline-*` cho preview trong `/documents`
  - `.gamma-*` và `.slide-preview-*` cho studio

### Kết luận cho pha thiết kế này

- Không viết lại toàn bộ visual language từ đầu
- Kế thừa ngôn ngữ card-based hiện có, nhưng chuẩn hóa để thêm ảnh và media workflow
- `DocumentList.js` là điểm bắt đầu chính của người dùng, `SlideStudio.js` là nơi tinh chỉnh sâu

## 3. North Star

Trải nghiệm mong muốn:

1. Người dùng upload hoặc chọn một document đã sẵn sàng.
2. Họ bấm tạo slide.
3. Hệ thống tạo outline trước, sau đó tạo từng slide text.
4. Tiếp theo hệ thống tìm hoặc sinh ảnh theo mô tả ảnh đã được local AI làm sạch.
5. Ngay tại `/documents`, người dùng đã thấy deck, ảnh mặc định của từng slide, và có thể đổi nhanh.
6. Khi cần chỉnh sâu hơn, họ mở `/slides/:documentId` với cùng deck, cùng ảnh đã chọn, cùng trạng thái.

Mục tiêu UX:

- Time to first meaningful preview ngắn
- Không ép người dùng phải vào Studio mới xem được deck
- Ảnh luôn minh bạch về nguồn: web hay generated, có attribution/license nếu cần
- Chuyển từ "đợi AI" sang "duyệt và chỉnh" càng sớm càng tốt

## 4. Reference Audit

## 4.1 Gamma - thứ sẽ học

Nguồn chính thức:

- Gamma cards: https://help.gamma.app/en/articles/11016396-what-are-cards-in-gamma-and-how-to-do-they-work
- Gamma image editing: https://help.gamma.app/en/articles/11028379-how-do-i-add-and-edit-images-in-gamma
- Gamma card styling / accent image: https://help.gamma.app/en/articles/11969695-how-do-i-style-cards-and-adjust-layout-settings-in-my-gamma
- Gamma AI image flow: https://help.gamma.app/en/articles/11047176-how-do-i-generate-images-with-ai-in-gamma

Pattern nên mượn:

- Card-based storytelling thay vì cảm giác giống PowerPoint cổ điển
- Outline sống, xuất hiện sớm hơn full content
- Per-card styling thay vì ép toàn deck một layout duy nhất
- Accent image theo nhiều vị trí: top, left, right, background
- Hero card có overlay rõ để giữ readability khi text nằm trên ảnh
- Mỗi card có thể thay ảnh riêng mà không phá toàn bộ deck

Pattern không mượn nguyên xi:

- Không clone bố cục hay tương tác của Gamma
- Không làm editor quá mở ở pha đầu như một trình design tự do
- Không cho phép card styling tùy ý hoàn toàn ở v1; cần guardrail để deck giữ nhất quán

## 4.2 Canva - thứ sẽ học

Nguồn chính thức:

- Canva AI presentations: https://www.canva.com/create/ai-presentations/
- Canva animated presentations: https://www.canva.com/create/animated-presentations/
- Canva visual hierarchy: https://www.canva.com/learn/visual-hierarchy/

Pattern nên mượn:

- First draft do AI tạo rất nhanh, sau đó người dùng chỉnh
- Media workflow rõ: có ảnh hiện tại, có thư viện ứng viên, có thao tác thay ảnh
- Tư duy template consistency: theme, spacing, hierarchy, emphasis
- Drag-and-drop mindset được chuyển hóa thành "quick replace" đơn giản hơn trong web app của ta
- Visual hierarchy 3 cấp: heading, supporting text, metadata
- Dùng kích thước, tương phản, spacing và focal point để dẫn mắt

Pattern không mượn nguyên xi:

- Không làm asset library khổng lồ kiểu Canva ở v1
- Không biến Studio thành full design surface với hàng chục thanh công cụ
- Không thêm animation editor hay timeline motion ở pha đầu

## 4.3 Kết luận tham chiếu

Thiết kế mục tiêu là:

- Cảm giác kể chuyện theo card như Gamma
- Có media workflow rõ và thực dụng như Canva
- Không clone UI của bên nào
- Ưu tiên tính reviewable và implementable trong codebase hiện tại

## 5. Information Architecture

## 5.1 `/documents`

Vai trò:

- Dashboard vận hành cho document
- Nơi theo dõi progress của document, question, slide, image
- Nơi preview deck đầu tiên
- Nơi đổi nhanh ảnh cho từng slide mà không cần mở Studio

Khối chính:

- Page header + stats
- Document card list
- Trong mỗi document card:
  - Status + progress panel
  - Slide deck panel
  - Inline outline
  - Inline slide preview
  - Image sourcing status
  - Quick replace ảnh
  - CTA mở Studio

## 5.2 `/slides/:documentId`

Vai trò:

- Workspace chỉnh chi tiết
- Nơi tinh chỉnh brief, đọc outline, xem toàn bộ canvas, sửa text và ảnh
- Nơi xuất HTML/PDF

Khối chính:

- Hero / deck summary
- Left rail: brief + live outline + deck actions
- Main canvas: preview các slide
- Right inspector: text, image, attribution, source, quick actions

## 5.3 Handoff giữa hai màn hình

Handoff bắt buộc giữ:

- `documentId`
- deck hiện tại
- slide đã chọn
- selected image của từng slide
- image candidate list đã tải
- trạng thái generation gần nhất

Mục tiêu handoff:

- Mở Studio từ `/documents` không tạo cảm giác "vào màn hình khác rồi mất công sức trước đó"
- Ảnh người dùng vừa đổi ở `/documents` phải xuất hiện đúng trong `/slides/:documentId`

## 6. Trải nghiệm tổng thể

## 6.1 Mental model cho người dùng

Người dùng cần hiểu hệ thống theo 3 lớp:

1. Local AI đọc tài liệu và suy luận nội dung slide
2. Slide engine tạo outline rồi tạo nội dung từng slide
3. Image worker tìm ảnh web an toàn hoặc sinh ảnh nếu cần

UI phải làm rõ:

- Nội dung slide đến từ tài liệu
- Ảnh đến từ một luồng riêng
- Mỗi slide có thể có hoặc không cần ảnh

## 6.2 Ưu tiên hiển thị

Thứ tự người dùng cần thấy:

1. Deck đã có hay chưa
2. Deck đang ở bước nào
3. Slide nào đã hoàn thành
4. Ảnh nào đã được chọn
5. Đổi ảnh ở đâu
6. Nếu lỗi, sửa ở đâu

## 7. Desktop Wireframes

## 7.1 `/documents`

Màn hình desktop mục tiêu:

```text
+----------------------------------------------------------------------------------+
| My Documents                                             [Refresh] [Live sync]   |
| Stats: Total docs | Processing | Ready | Slides | Images ready                  |
+----------------------------------------------------------------------------------+

+----------------------------------------------------------------------------------+
| Document Card                                                                    |
| File name.pdf                                             [Completed] [Open]     |
| Uploaded time | size | questions | deck status                                   |
| Summary / status hint                                                            |
|                                                                                  |
| +--------------------------------+  +------------------------------------------+ |
| | Progress rail                  |  | Slide deck panel                         | |
| | - Document processing          |  | Deck title                               | |
| | - Slide generation             |  | Subtitle / brief summary                 | |
| | - Image sourcing               |  | [Open Studio] [HTML/PDF] [Create images] | |
| | - ETA / error                  |  |                                          | |
| +--------------------------------+  +------------------------------------------+ |
|                                                                                  |
| Outline                                                                          |
| [1 Cover] [2 Section] [3 Content] [4 Quote] ...                                 |
|                                                                                  |
| Inline slide preview grid                                                        |
| +----------------------+ +----------------------+ +----------------------+        |
| | Slide 1              | | Slide 2              | | Slide 3              |        |
| | Hero image           | | Selected image       | | No image needed      |        |
| | Heading              | | Heading              | | Heading              |        |
| | Body preview         | | Body preview         | | Body preview         |        |
| | Source badge         | | Source badge         | | Text-only badge      |        |
| | [Doi anh]            | | [Doi anh]            | | [Them anh]           |        |
| +----------------------+ +----------------------+ +----------------------+        |
|                                                                                  |
| Candidate tray for expanded slide                                                |
| [img 1 selected] [img 2] [img 3] [img 4] [Generate fallback]                    |
+----------------------------------------------------------------------------------+
```

Giải nghĩa vùng:

- Header giữ cảm giác dashboard vận hành, không phải editor
- Progress rail đứng riêng để tách content progress với media progress
- Deck panel là khối tóm tắt và hành động
- Inline preview là nơi review nhanh
- Candidate tray chỉ xuất hiện khi người dùng đang thao tác với một slide cụ thể

## 7.2 `/slides/:documentId`

Màn hình desktop mục tiêu:

```text
+----------------------------------------------------------------------------------+
| Deck Hero                                                                        |
| Deck title                           Theme | Slides | Image status | Export      |
| Deck subtitle / narrative goal                                                    |
+----------------------------------------------------------------------------------+

+-------------------------------+ +----------------------------------------------+
| Left rail                     | | Main canvas                                  |
|                               | |                                              |
| Deck brief                    | | Canvas toolbar                               |
| - Audience                    | | [Reading mode] [Show all] [Export HTML/PDF] |
| - Tone                        | |                                              |
| - Narrative goal              | | +------------------------------------------+ |
| - Theme picker                | | | Slide card                               | |
|                               | | | Meta / type / status                     | |
| Live outline                  | | | Heading / subheading / goal              | |
| [1] [2] [3] [4] ...           | | | Hero or accent image                     | |
|                               | | | Body blocks                              | |
| Deck actions                  | | | Attribution footer                       | |
| [Regenerate] [Populate img]   | | +------------------------------------------+ |
+-------------------------------+ |                                              |
                                  | +------------------------------------------+ |
                                  | | Slide card                               | |
                                  | +------------------------------------------+ |
                                  +----------------------------------------------+

+----------------------------------------------+
| Right inspector                               |
| Selected slide                               |
| Text fields                                  |
| Image block                                  |
| - Selected image preview                     |
| - Source / provider / license                |
| - [Doi anh] [Bo anh] [Thu lai]               |
| Candidate list                               |
| Attribution                                  |
| Quality / low confidence                     |
+----------------------------------------------+
```

Giải nghĩa vùng:

- Left rail là "điều khiển cấp deck"
- Main canvas là "đọc giống deck thật"
- Right inspector là "chỉnh cấp slide"
- Layout 3 vùng giúp tách global decisions khỏi local decisions

## 8. Primary Flows

## 8.1 Flow A - Tạo deck mới

1. Từ `/documents`, user chọn tài liệu đã `Completed`
2. Bấm `Tạo slide dần dần` hoặc `Tạo lại slide`
3. Card chuyển sang trạng thái generating
4. Outline xuất hiện trước
5. Từng slide card được lấp dần nội dung
6. Khi text hoàn tất, image worker bắt đầu cho các slide cần ảnh

Kỳ vọng UX:

- User thấy kết quả từng phần, không chờ full deck xong mới xem được

## 8.2 Flow B - Live generation

Trạng thái hiển thị theo thứ tự:

1. `Validating document`
2. `Generating outline`
3. `Generating slides`
4. `Slides ready`
5. `Sourcing images`
6. `Image candidates ready`

Quy tắc:

- Progress nội dung và progress ảnh không dùng chung một thanh
- Progress ảnh phải hiện rõ đây là luồng riêng

## 8.3 Flow C - Ảnh mặc định cho từng slide

1. Mỗi slide nếu `needsImage = true` sẽ có một ảnh được chọn mặc định
2. Ảnh này xuất hiện ngay trong inline preview
3. Nếu là ảnh web, hiển thị badge `Web`
4. Nếu là ảnh generated, hiển thị badge `AI Generated`
5. Nếu slide không cần ảnh, hiển thị `Text-only`

Kỳ vọng UX:

- User không phải bấm thêm mới thấy ảnh đầu tiên

## 8.4 Flow D - Đổi ảnh nhanh ở `/documents`

1. User bấm `Đổi ảnh` trên một slide
2. Card đó mở candidate tray ở ngay dưới grid
3. Tray hiển thị 4 ảnh ứng viên
4. User bấm vào một ảnh
5. UI cập nhật optimistic selected image
6. Nếu save lỗi, rollback và báo lỗi tại chỗ

Kỳ vọng UX:

- Nhanh như chọn một biến thể, không như mở modal phức tạp

## 8.5 Flow E - Fallback khi không có ảnh web hợp lệ

1. Slide hiển thị badge `Không có ảnh web phù hợp`
2. Hệ thống gợi ý `Sinh ảnh từ mô tả đã redacted`
3. User có thể bấm `Generate fallback`
4. Candidate tray nạp thêm ảnh generated

Quy tắc:

- Phải giải thích vì sao fallback xuất hiện
- Không âm thầm chuyển sang ảnh generated mà không gắn badge

## 8.6 Flow F - Mở Studio và chỉnh sâu

1. Từ `/documents`, user bấm `Mở Studio`
2. Studio mở đúng deck hiện tại
3. Slide đầu tiên hoặc slide vừa thao tác gần nhất được focus
4. User chỉnh text, notes, hoặc đổi ảnh trong inspector
5. Khi export, ảnh đang chọn được dùng cho HTML/PDF

## 9. Component Spec

## 9.1 `Document card`

Nhiệm vụ:

- Là container vận hành chính cho một tài liệu

Thông tin hiển thị:

- File name
- Document status
- Time / size / question count
- Status hint
- Progress rails
- Deck summary
- Inline outline
- Inline slide preview

Actions:

- `View Analysis`
- `Slide Studio`
- `Tạo slide`
- `Tạo lại slide`
- `Tạo ảnh`
- `Delete`

States:

- document chưa xong
- text generation đang chạy
- image generation đang chạy
- deck đã sẵn sàng
- deck thất bại

## 9.2 `Inline slide preview`

Nhiệm vụ:

- Cho xem nhanh 2-3 slide đầu hoặc các slide quan trọng

Thông tin hiển thị:

- slide index
- slide type
- heading
- subheading ngắn
- 1-2 body blocks
- selected image nếu có
- badge nguồn ảnh
- trạng thái low confidence nếu có

Quy tắc layout:

- Ảnh nằm trên cùng hoặc bên phải tùy slide type
- Không dùng full editor controls ở đây

## 9.3 `Image candidate tray`

Nhiệm vụ:

- Cho thay ảnh nhanh theo ngữ cảnh của một slide

Thông tin hiển thị:

- 4 thumbnails
- selected state
- source badge
- provider
- license/attribution tóm tắt
- nút `Generate fallback` nếu cần

Tương tác:

- chỉ mở cho một slide mỗi lần
- đóng khi user chọn xong hoặc mở slide khác

## 9.4 `Generation progress panel`

Nhiệm vụ:

- Tách 3 lớp tiến trình:
  - document processing
  - slide generation
  - image sourcing

Thông tin hiển thị:

- stage label
- message
- detail
- eta
- current / total nếu có
- error nếu có

Quy tắc:

- Thanh progress của nội dung và media phải tách riêng

## 9.5 `Slide canvas card`

Nhiệm vụ:

- Render slide giống output thật nhất có thể trong web

Phải hỗ trợ:

- text-only slide
- hero image slide
- side accent image slide
- background image slide
- section divider

Thành phần:

- slide meta
- heading / subheading / goal
- image zone
- body zone
- notes / attribution zone

## 9.6 `Right-side inspector`

Nhiệm vụ:

- Chỉnh chi tiết cấp slide

Tab/nhóm nội dung:

- Content
- Image
- Source / attribution
- Quality

Tác vụ chính:

- sửa heading
- sửa subheading
- sửa body
- xem ảnh đang chọn
- đổi ảnh
- bỏ ảnh
- thử lại image sourcing

## 9.7 `Attribution / license badge`

Nhiệm vụ:

- Tạo sự minh bạch cho ảnh

Hiển thị tối thiểu:

- source type
- provider/domain
- license label
- short attribution

Vị trí:

- `/documents`: bản rút gọn
- `/slides/:documentId`: bản đầy đủ hơn trong inspector
- export: footer nhỏ nếu ảnh web yêu cầu attribution

## 10. Visual System

## 10.1 Typographic hierarchy

Chốt 3 cấp rõ ràng:

- Cấp 1: deck title / slide heading
- Cấp 2: subheading / goal / section titles
- Cấp 3: metadata / badges / attribution / helper text

Quy tắc:

- Không nhiều hơn 3 cấp chính trên cùng một card
- Heading phải là entry point đầu tiên
- Metadata luôn lùi về sau bằng contrast và cỡ chữ

## 10.2 Layout rhythm

Quy tắc:

- Card lớn, bo tròn rộng, khoảng trắng hào phóng
- Mỗi vùng chính phải có breathing room rõ
- Grid preview ưu tiên ổn định hơn là dày đặc
- Padding trong card phải đủ để ảnh và text không dính nhau

## 10.3 Theme system

Kế thừa 4 theme hiện có và chuẩn hóa thành token:

- `editorial-sunrise`
- `paper-mint`
- `cobalt-grid`
- `midnight-signal`

Token cần chốt:

- background
- card
- card-strong
- text
- muted-text
- accent
- accent-soft
- border
- divider
- image-overlay
- success
- warning
- danger

## 10.4 Radius, elevation, motion

Radius:

- page card: 28-32px
- inner card: 18-24px
- pill: full radius

Elevation:

- dashboard card: nhẹ
- selected card: vừa
- active candidate: rõ nhưng không nặng

Motion:

- chỉ dùng motion ngắn cho reveal, selected state, loading skeleton
- tránh motion làm người dùng hiểu nhầm progress đang thực sự tăng

## 10.5 Quy tắc ảnh

Các layout ảnh được phép trong v1:

- `hero`
- `background`
- `side-accent-right`
- `side-accent-left`
- `none`

Quy tắc:

- Nếu text nằm trên ảnh thì overlay là bắt buộc
- Overlay phải đủ tương phản cho mobile và export
- Nếu ảnh quá bận hoặc quá dọc thì fallback sang side-accent hoặc text-only
- Section divider và quote slide có thể không cần ảnh

## 11. Responsive Behavior

## 11.1 Desktop

- Layout chính
- `/documents`: card list với grid preview
- `/slides/:documentId`: 3 vùng left rail + canvas + inspector

## 11.2 Tablet

- Left rail thành panel thu gọn hoặc drawer
- Inspector có thể xuống dưới canvas
- Candidate tray vẫn inline nhưng giảm số cột thumbnail

## 11.3 Mobile

- `Document-first`, không cố giữ editor phức tạp
- `/documents`: mỗi document card là luồng chính
- `/slides/:documentId`: chuyển thành stacked sections
- Ảnh preview vẫn có nhưng không full-bleed dài quá
- Progress và candidate tray hiển thị tuần tự thay vì đa cột

## 12. States và Edge Cases

## 12.1 Empty states

- chưa có document
- document chưa có deck
- deck chưa có outline
- slide chưa có ảnh

## 12.2 Generating states

- `processing-document`
- `generating-outline`
- `generating-slides`
- `sourcing-images`
- `generating-image-fallback`

## 12.3 Partial-ready

- outline có nhưng slide chưa đủ
- slide text xong nhưng ảnh chưa xong
- chỉ một số slide có ảnh

## 12.4 Error / failure

- document processing fail
- slide generation fail
- image sourcing fail
- image selection save fail
- export fail

## 12.5 Content-specific states

- `no-image-needed`
- `no-license-safe-image`
- `generated-only`
- `low-confidence`
- `text-only-by-user`

## 12.6 Skeleton rules

Phải phân biệt:

- skeleton cho text slide: thanh line ngang
- skeleton cho image: block thumbnail với shimmer riêng

Lý do:

- User cần hiểu đang chờ nội dung hay đang chờ media

## 13. UI Contract cho pha code sau

Pha này không đổi API. Tuy nhiên design cần chốt view-model để pha code bám theo nhất quán.

## 13.1 `deck generation state`

```ts
type DeckGenerationState = {
  status: 'queued' | 'running' | 'completed' | 'failed';
  stage:
    | 'validating-document'
    | 'generating-outline'
    | 'generating-slides'
    | 'sourcing-images'
    | 'completed'
    | 'failed';
  stageLabel: string;
  message: string;
  detail?: string | null;
  percent?: number | null;
  current?: number | null;
  total?: number | null;
  unitLabel?: string | null;
  elapsedSeconds?: number | null;
  estimatedRemainingSeconds?: number | null;
  error?: string | null;
};
```

## 13.2 `slide image state`

```ts
type SlideImageState = {
  needsImage: boolean;
  status:
    | 'not-requested'
    | 'queued'
    | 'sourcing-web'
    | 'generating-fallback'
    | 'ready'
    | 'failed'
    | 'no-image-needed'
    | 'no-license-safe-image';
  message?: string | null;
  detail?: string | null;
  candidateCount?: number;
  selectedImageKey?: string | null;
  error?: string | null;
};
```

## 13.3 `image candidate`

```ts
type ImageCandidate = {
  key: string;
  sourceType: 'web' | 'generated';
  provider: string;
  originUrl?: string | null;
  localAssetUrl: string;
  thumbnailUrl?: string | null;
  altText: string;
  licenseLabel?: string | null;
  attributionText?: string | null;
  width?: number | null;
  height?: number | null;
  score?: number | null;
  isSelected?: boolean;
};
```

## 13.4 `selected image`

```ts
type SelectedImageViewModel = {
  key: string;
  localAssetUrl: string;
  sourceType: 'web' | 'generated';
  provider: string;
  altText: string;
  licenseLabel?: string | null;
  attributionText?: string | null;
  layoutMode?: 'hero' | 'background' | 'side-accent-right' | 'side-accent-left';
};
```

## 13.5 `license / attribution display`

```ts
type AttributionDisplay = {
  visible: boolean;
  compactText?: string | null;
  fullText?: string | null;
  sourceUrl?: string | null;
  licenseLabel?: string | null;
  requireFooterInExport: boolean;
};
```

## 14. Screen-level Spec

## 14.1 `/documents` chi tiết

Màn hình này ưu tiên:

- operational clarity
- first preview
- quick replacement

Hành vi mong muốn:

- deck panel chỉ xuất hiện khi document đủ điều kiện hoặc đang sinh slide
- image sourcing state chỉ xuất hiện khi deck đã có hoặc image worker đã chạy
- user có thể review 3 slide đầu mà không cần rời trang
- nếu một slide đang mở candidate tray thì tray gắn vào chính document card đó

Không nên có:

- modal nặng cho đổi ảnh
- editor text đầy đủ ngay trong list
- quá nhiều badge cạnh tranh với nội dung

## 14.2 `/slides/:documentId` chi tiết

Màn hình này ưu tiên:

- narrative editing
- media replacement
- export readiness

Hành vi mong muốn:

- chọn slide từ outline sẽ focus canvas và inspector cùng lúc
- edit text và image không tranh quyền nhau
- slide card trong canvas phải gần output đủ để review, nhưng inspector mới là nơi chỉnh
- export CTA luôn hiện nhưng disabled nếu deck chưa đủ điều kiện

Không nên có:

- quá nhiều toolbar nổi
- drag-drop tự do ở v1
- nhiều modal lồng nhau

## 15. Handoff contract giữa `/documents` và `/slides/:documentId`

Thông tin cần được giữ đồng nhất:

- deck title
- theme
- outline
- item status
- selected image
- candidate list
- attribution
- low confidence marker

Nếu user đã mở candidate tray ở `/documents`, khi vào studio hệ thống nên:

- focus đúng slide đó nếu có state điều hướng
- hoặc ít nhất mở studio ở slide vừa thao tác cuối cùng

## 16. Review Checklist

Checklist duyệt thiết kế:

- Flow từ `Document tab` sang `Slide Studio` có liền mạch không
- Người dùng có luôn thấy ảnh hiện tại là ảnh nào, nguồn nào, đổi ở đâu không
- Trạng thái local AI và internet image worker có được tách bạch và dễ hiểu không
- Layout có đọc tốt khi slide chưa có ảnh không
- Layout có đọc tốt khi ảnh dọc hoặc crop xấu không
- Layout có chỗ xử lý badge `no-license-safe-image` rõ ràng không
- Có đường lui về text-only rõ ràng không
- HTML/PDF export có được tính từ đầu không
- Attribution có hiển thị đủ mà không làm bẩn card không
- Candidate tray có nhanh và nhẹ hơn mở modal không

## 17. Non-goals cho pha đầu

Không nằm trong pha đầu:

- full drag-and-drop editor
- animation timeline
- template marketplace
- asset library khổng lồ kiểu Canva
- batch styling mọi card bằng AI
- collaboration realtime nhiều người

## 18. Assumptions

- Đây là spec thiết kế để duyệt trước khi code UI/flow
- Pha này chưa đổi API, DB hay backend contract
- Pha code sau sẽ bám source UI thật đang chạy:
  - `client/src/components/DocumentList.js`
  - `client/src/components/SlideStudio.js`
  - `client/src/App.css`
- Theme hiện có trong repo sẽ được tận dụng, không thay thương hiệu toàn cục
- Image worker internet là luồng riêng, không làm local AI phải gửi raw document content ra ngoài

## 19. Tóm tắt quyết định cuối

- Chọn hướng `Gamma x Canva`, không clone bên nào
- `/documents` là nơi review và quick replace
- `/slides/:documentId` là nơi chỉnh sâu và export
- Một slide mặc định có 1 ảnh đã chọn nếu cần ảnh
- `Đổi ảnh` mở candidate tray inline, không dùng modal ở v1
- Badge nguồn và attribution là bắt buộc khi dùng ảnh web
- Progress nội dung và progress ảnh phải tách riêng
- Overlay là bắt buộc khi text nằm trên ảnh nền

## 20. References

- Gamma cards: https://help.gamma.app/en/articles/11016396-what-are-cards-in-gamma-and-how-to-do-they-work
- Gamma image editing: https://help.gamma.app/en/articles/11028379-how-do-i-add-and-edit-images-in-gamma
- Gamma card styling / accent image: https://help.gamma.app/en/articles/11969695-how-do-i-style-cards-and-adjust-layout-settings-in-my-gamma
- Gamma AI image flow: https://help.gamma.app/en/articles/11047176-how-do-i-generate-images-with-ai-in-gamma
- Canva AI presentations: https://www.canva.com/create/ai-presentations/
- Canva animated presentations: https://www.canva.com/create/animated-presentations/
- Canva visual hierarchy: https://www.canva.com/learn/visual-hierarchy/
