# ROADMAP - ELearn Game Platform

Cap nhat: 2026-03-23

## 1. Muc tieu roadmap

- Muc tieu cua 2 tuan tiep theo la nang tam he thong tu "MVP chay duoc" thanh "ban demo co suc thuyet phuc", uu tien trai nghiem nguoi dung truoc.
- Roadmap nay xep thu tu uu tien theo tac dong thuc te den hoc sinh, sinh vien, giang vien huong dan, va kha nang demo PBL.
- Nguyen tac chot cho sprint nay:
  - UI/UX duoc dua len uu tien cao nhat.
  - Chi mo rong backend khi no phuc vu truc tiep cho UX, game mode, hoac slide template.
  - Khong mo them scope ha tang lon neu chua can cho demo.

## 2. Hien trang repo

- Da co cac luong chinh:
  - Upload tai lieu.
  - OCR va trich xuat text.
  - AI phan tich noi dung.
  - Sinh cau hoi.
  - Hoc bang quiz va flashcards.
  - Sinh va chinh sua slide trong Slide Studio.
- Da co nang cap AI gan day:
  - OCR multi-pass va cleanup text manh hon.
  - Router nhieu model cho analysis, generation, verification.
  - Verifier AI va auto-repair 1 vong cho question va slide.
- Diem yeu lon nhat hien tai:
  - UI/UX chua dong bo, gia tri backend chua duoc the hien tot tren frontend.
  - Game mode con it, chua tao cam giac "hoc ma van thay choi".
  - Chua co slide template san de demo nhanh.
  - Chua co benchmark va timing log de do chat luong that.
  - Job state van dua vao memory, chua ben vung neu restart app.
  - Auth JWT co ban da co, nhung chua hardening production.
  - Chua co test tu dong co he thong.

## 3. Thu tu uu tien

### P0 - UI/UX redesign cho core journey

- Muc tieu: Lam lai trai nghiem tu upload -> phan tich -> tao cau hoi -> choi game -> tao slide thanh mot flow ro rang, dep, de demo, de hoc.
- Vi sao uu tien cao nhat:
  - Day la thu user nhin thay dau tien.
  - Backend hien tai da du nang luc de "nang cap cam nhan san pham" ma khong can doi them he thong lon.
  - Cai thien UX se giup game mode va slide template phat huy gia tri ngay.
- Pham vi:
  - Lam lai dashboard tai lieu.
  - Lam lai trang thai progress, error, retry, quality badge.
  - Lam lai flow review cau hoi va flow choi game.
  - Lam lai Slide Studio theo huong de preview, de chon template, de sua.
  - Chuan hoa visual system: color, spacing, typography, card, button, empty state, loading state.
- Done when:
  - Nguoi moi vao he thong co the hieu 3 hanh dong chinh trong 10 giay: "Tai tai lieu", "Hoc ngay", "Tao slide".
  - Moi AI job deu co progress va error message ro rang.
  - Giao dien desktop/mobile khong bi vo layout.
  - Quiz, flashcard, slide studio co cung mot ngon ngu thiet ke.
- Danh gia:
  - Tac dong: Rat cao
  - Do kho: Trung binh
  - Do uu tien: So 1

### P1 - Mo rong game mode cho hoc sinh, sinh vien

- Muc tieu: Tang "do choi duoc" va "do quay lai hoc" bang 2 game mode moi de bo sung cho quiz/flashcards.
- Vi sao dung ngay sau UI/UX:
  - Neu giao dien dep hon nhung gameplay van don dieu thi gia tri san pham van cham tang.
  - Game mode moi tao diem khac biet ro rang khi demo.
- Uu tien game mode:
  - `Streak Mode`: tra loi lien tiep de giu chuoi, sai la reset.
  - `Match Pairs`: ghep khai niem - dinh nghia, thuat ngu - giai thich.
  - `Weakness Mode`: hoi lai phan da sai hoac low-confidence.
- Pham vi sprint nay:
  - Lam chac 2 mode dau tien.
  - `Weakness Mode` de o backlog neu con thoi gian.
- Done when:
  - User co the chon it nhat 2 game mode moi tu cung mot document.
  - Ket qua choi hien thi tien do, diem, va feedback ro rang.
  - Game moi tai su dung duoc question data hien co, khong can pipeline rieng.
- Danh gia:
  - Tac dong: Cao
  - Do kho: Trung binh
  - Do uu tien: So 2

### P2 - Slide templates san de demo nhanh

- Muc tieu: Bien Slide Studio tu "co the generate" thanh "co the tao deck dep, co dinh huong, de chon nhanh".
- Vi sao xep sau game:
  - Slide da co MVP backend.
  - Gia tri tiep theo nam o viec cho user chon style va muc dich, khong nam o them 1 pipeline moi.
- Template uu tien:
  - `On Tap Nhanh`
  - `Bai Giang 10 Phut`
  - `Tom Tat Chuong`
  - `PBL Defense`
- Pham vi:
  - Them template picker.
  - Moi template co theme, tone, audience, outline mac dinh.
  - Preview slide phai thay ro su khac nhau giua cac template.
- Done when:
  - User co the generate slide theo it nhat 3 template co phong cach khac nhau.
  - Template duoc ap dung xuyen suot tu outline den preview.
  - Slide co the demo tot tren desktop va mobile.
- Danh gia:
  - Tac dong: Cao
  - Do kho: Trung binh
  - Do uu tien: So 3

### P3 - Do luong chat luong AI/OCR

- Muc tieu: Dung so lieu that de toi uu OCR, summary, question, slide thay vi dieu chinh bang cam tinh.
- Vi sao chua dat len tren P0/P1/P2:
  - Day la tang "lam san pham manh that su", nhung user khong nhin thay ngay bang UX va game.
  - Van can lam trong sprint de tranh toi uu mu.
- Hang muc:
  - Tao bo benchmark 20-50 tai lieu mau.
  - Log timing tung stage: OCR, analysis, generation, verification, auto-repair.
  - Log score verifier cho question va slide.
  - Them confidence theo trang OCR va re-OCR cho trang diem thap.
  - Them profile `fast / balanced / quality`.
- Done when:
  - Co bang so lieu de biet flow nao cham nhat va flow nao loi nhieu nhat.
  - Co the so sanh truoc/sau moi lan doi model hoac doi prompt.
  - OCR khong can re-run toan bo file neu chi 1 vai trang diem thap.
- Danh gia:
  - Tac dong: Rat cao
  - Do kho: Trung binh den cao
  - Do uu tien: So 4

### P4 - Do ben he thong va kha nang mo rong

- Muc tieu: Giam technical debt de he thong san sang cho user that va du an lon hon.
- Hang muc:
  - Chuyen job state tu memory sang persistent store.
  - Hoan thien auth production hardening va ownership edge cases.
  - Them test tu dong cho core flows.
  - Them health checks va logging co cau truc.
  - Them validation va failure handling day du hon.
- Ly do xep sau:
  - Rat quan trong cho trung han.
  - Khong phai diem tao khac biet lon nhat cho demo 2 tuan toi.
- Done when:
  - Restart app khong lam mat job dang xu ly.
  - Moi tai lieu va session hoc gan voi user that.
  - Core flows co test co y nghia.
- Danh gia:
  - Tac dong: Cao
  - Do kho: Cao
  - Do uu tien: So 5

## 4. Roadmap 2 tuan

### Tuan 1 - Dat lai trai nghiem cot loi

- Ngay 1:
  - Audit UX toan bo flow hien tai.
  - Chot visual direction.
  - Chot danh sach component can lam lai.
- Ngay 2:
  - Dung design tokens va component base.
  - Chuan hoa button, badge, card, progress, modal, empty state.
- Ngay 3:
  - Lam lai workspace dashboard/list va source cards.
  - Them AI timeline va action chinh ro rang.
- Ngay 4:
  - Lam lai flow generate question.
  - Them review state cho quality, verifier, auto-repair.
- Ngay 5:
  - Lam lai `SlideStudio`.
  - Them khuon preview ro rang va san cho template picker.

### Tuan 2 - Tang gia tri demo va gia tri hoc tap

- Ngay 6:
  - Them `Streak Mode`.
- Ngay 7:
  - Them `Match Pairs`.
- Ngay 8:
  - Them slide template picker va 3-4 template dau tien.
- Ngay 9:
  - Them timing log va benchmark co ban.
  - Do OCR, question, slide tren bo tai lieu mau.
- Ngay 10:
  - Polish giao dien.
  - Fix edge cases cho demo.
  - Chot script demo va screenshot.

## 5. Scope lock cho sprint nay

- Khong mo rong qua 2 game mode moi trong 2 tuan nay.
- Khong lam auth day du neu no khong phai blocker.
- Khong doi kien truc lon o backend neu UX van co the di truoc.
- Khong them qua 4 slide template o sprint dau.
- Khong dong thoi theo duoi qua nhieu game "hoanh trang" nhu PvP realtime hay co che phuc tap.

## 6. Backlog sau sprint 2 tuan

- `Weakness Mode` va learning history nang cao.
- `Ask Document` hoac hoi dap truc tiep tren tai lieu.
- Teacher review flow cho question/slide low-confidence.
- Auth hardening, ownership audit, va dashboard ca nhan.
- Persistent jobs va job recovery.
- Test tu dong va deployment checklist.

## 7. Acceptance checklist cho roadmap nay

- [x] Thu tu uu tien moi dat UI/UX len hang dau.
- [x] Game mode moi va slide template duoc dua vao trung tam sprint.
- [x] Slide duoc xem la module da co MVP, khong con bi mo ta nhu "chua ton tai".
- [x] Roadmap co moc 2 tuan cu the de trien khai.
- [x] Van giu duong nang cap tiep theo cho benchmark, reliability, auth, va test.
