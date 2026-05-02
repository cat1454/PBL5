# Quy tắc sửa prompt cho Agent

## Mục tiêu

Khi người dùng đưa prompt mơ hồ, sai cấu trúc, hoặc đề bài dễ dẫn đến output kém, Agent được phép gợi ý một bản prompt viết lại rõ hơn.

Điều này không có nghĩa là đẩy việc ngược lại cho người dùng. Mặc định:

- Nếu Agent có thể tự giả định an toàn và tiếp tục làm được, Agent vẫn nên làm.
- Agent chỉ nên gợi ý viết lại prompt khi prompt thiếu phần cốt lõi hoặc đề bài có nhiều cách hiểu khác nhau.

## Khi nào nên gợi ý viết lại prompt

Agent nên gợi ý viết lại prompt khi:

- Không rõ mục tiêu cuối cùng.
- Không rõ file, module, endpoint, component hoặc nguồn sự thật.
- Yêu cầu vừa muốn hotfix nhỏ, vừa muốn refactor rộng.
- Mô tả bug thiếu hành vi hiện tại và hành vi mong muốn.
- Yêu cầu review nhưng thực chất lại muốn implement.
- Phạm vi sửa không rõ, dễ khiến Agent sửa lan sang phần không liên quan.
- Có nhiều hướng xử lý khác nhau nhưng trade-off chưa được chốt.

## Cách gợi ý đúng

Agent nên trả theo 3 bước:

1. Nêu ngắn gọn vấn đề của prompt hiện tại.
2. Đưa ra một bản prompt viết lại có thể dùng ngay.
3. Nếu hợp lý, nêu rõ Agent sẽ tạm giả định hướng nào để tiếp tục trong lúc chờ xác nhận.

## Mẫu phản hồi

```md
Prompt hiện tại còn thiếu:

- ...
- ...

Bạn có thể prompt lại theo mẫu này:

Mục tiêu:
...

Nguồn sự thật:
...

Hành vi hiện tại:
...

Hành vi mong muốn:
...

Phạm vi sửa:
...

Không được làm:
...

Cách verify:
...
```

## Điều cần tránh

- Không phán xét người dùng là "prompt sai".
- Không bắt người dùng viết lại khi Agent vẫn có thể tự lần context an toàn.
- Không đặt quá nhiều câu hỏi mở cùng lúc.
- Không dùng việc sửa prompt như lý do để né task.
- Không yêu cầu xác nhận nếu hướng xử lý đã đủ rõ và rủi ro thấp.

## Nguyên tắc thực chiến

Agent phải ưu tiên hoàn thành task nếu có đủ context. Việc gợi ý viết lại prompt chỉ dùng để giảm mơ hồ, khóa scope, hoặc tránh sửa sai hướng.

Nếu prompt chưa hoàn hảo nhưng vẫn đủ thông tin để xử lý an toàn, Agent nên nêu giả định ngắn gọn rồi tiếp tục làm.
