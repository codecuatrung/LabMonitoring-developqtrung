# LabMonitoring AI Scheduling Assistant

## 1. Goal And Scope

The assistant helps a coordinator draft, check, and submit teaching schedules and duty assignments. It does not silently publish changes, invent availability, or replace the coordinator's approval. The expected happy path is 3-8 turns; a conflict-resolution or incomplete-data path may take 8-15 turns.

The assistant needs the term and date range, class and subject, instructor, room, start/end time, recurrence rule, and whether the session is a make-up. For duties it also needs the room/area, required headcount, assignees or assignment preference, and approval state. It must read current records and scheduling rules before making a recommendation.

The user needs a concise draft, any missing fields, concrete conflicts with dates and records, viable alternatives, a final before/after summary, and an explicit saved/not-saved result. The app should show the same draft and outcome in its schedule or duty table.

## 2. Dialogue Structure

Use a guided workflow with mixed initiative. The user states the outcome; the assistant asks only for missing or ambiguous constraints, checks records, proposes choices, and requests confirmation before writing. This fits scheduling because many constraints are structured and collisions are costly, while users should remain in control of the final assignment.

Normal flow: user request → assistant extracts fields → user resolves only missing details → assistant grounds and checks → assistant presents a preview → user confirms or edits → assistant writes → UI shows the result and audit entry.

## 3. Conversation Flow Diagram

```text
[User asks to schedule or assign]
             |
             v
[Extract fields and intent]
             |
       missing or ambiguous?
        /             \
      yes              no
       |                |
[Ask one focused]      v
[clarifying question] [Read current term, schedules, duties, rooms, people]
       |                |
       +------<---------+
                        v
              [Check constraints]
                 /           \
          conflict found    clear
              |               |
      [Offer alternatives]    v
              |         [Show grounded preview]
              +------->       |
                        [User confirms / edits / cancels]
                            /        |        \
                         edit     confirm    cancel
                           |          |          |
                    [Re-check] [Write once] [No write]
                                      |
                                      v
                         [Show result and audit status]
```

## 4. Initiative Map

| Stage | Initiative | Handoff trigger |
| --- | --- | --- |
| State the goal | User | User submits a natural-language request or selects a scheduling action. |
| Fill missing constraints | Assistant asks; user answers | Assistant detects a required field, ambiguous date, or unclear series scope. |
| Ground data | Assistant | User's required fields are sufficient; assistant reads the current schedule, room, term, and duty data. |
| Resolve a conflict | Assistant proposes; user chooses | A current record violates teacher, room, class, availability, or headcount rules. |
| Review draft | Assistant summarizes; user owns approval | All required checks pass or the user selects an alternative. |
| Commit | User confirms; assistant executes | Explicit confirmation names the exact draft/version. Any edit returns to validation. |
| Verify outcome | Assistant reports; user resumes control | Save succeeds or fails; the UI refreshes and presents the record/audit result. |

## 5. Modality Specification

| Stage | Primary input/output | Supporting modality | Transition |
| --- | --- | --- | --- |
| Request | Text input | Quick action for “create schedule” or “assign duty” | Natural language is parsed into structured fields. |
| Clarification | Short text question and answer | Date picker, person/room selectors, recurrence control | Free text is converted into explicit dates and IDs/names. |
| Grounding | Text status (“checking current week”) | Schedule table, room availability and conflict rows | Data evidence is shown next to the proposed result. |
| Preview | Structured summary card | Calendar/table preview with conflict indicators | User can edit a field directly or confirm the exact draft. |
| Result | Text receipt | Updated schedule/duty table and audit entry | Outcome links back to the affected record. |

Text and structured controls are primary. Calendar/table views are supporting evidence. Voice, email, and external calendar integration are not assumed in the first version.

## 6. Repair Protocol

### Repair A: Ambiguous date

Assistant: “Bạn nói ‘tuần tới’. Bạn muốn tuần từ thứ Hai 09/11 đến Chủ nhật 15/11/2026, đúng không?”

User correction: “Không, tuần học bắt đầu thứ Hai 16/11.”

Assistant repair: “Đã đổi phạm vi sang 16–22/11/2026. Mình sẽ kiểm tra lại toàn bộ lần lặp trong khoảng này.” The new range replaces the old draft; no record is written before confirmation.

### Repair B: Collision or unavailable person

Assistant: “Phòng A.203 đã có lịch 08:00–10:00 ngày 18/11. Có thể chuyển sang B.105 lúc 08:00 hoặc giữ A.203 lúc 10:30. Bạn chọn phương án nào?”

User correction: “Giữ phòng A.203 nhưng chuyển ca sang 10:30.”

Assistant repair: “Đã sửa giờ bắt đầu trong bản nháp. Mình kiểm tra lại cả giáo viên, lớp và phòng cho mọi ngày lặp.” If a conflict remains, do not offer a save action.

### Repair C: Wrong recurrence scope

Assistant: “Bạn muốn hủy riêng buổi 20/11 hay hủy cả chuỗi hàng tuần?”

User correction: “Chỉ buổi 20/11; các tuần còn lại giữ nguyên.”

Assistant repair: “Đã giới hạn thay đổi vào bản ghi ngày 20/11. Các buổi khác không nằm trong thao tác này.” The preview lists every affected record before confirmation.

Users can interrupt at any point with “sửa …”, “chỉ áp dụng cho …”, “quay lại bản trước”, or “hủy”. An interruption invalidates the pending confirmation and forces a fresh validation.

## 7. Grounding Checkpoints

1. Normalize relative dates using the configured locale and timezone; show the resolved dates before checking.
2. Read current records at request time. Identify conflicts by record, date, time, resource, and status; ignore only records explicitly canceled.
3. For recurring schedules, validate every generated occurrence and show the recurrence limit and any excluded dates.
4. Before writing, display the exact set of records to create, edit, or cancel and require a fresh confirmation for that version.
5. After writing, refresh from the API and report the persisted state. If the API fails or the data changed during review, do not claim success; reload and revalidate.

## 8. Progressive Disclosure

At the start, expose only schedule/duty drafting, a visible “review before saving” rule, and basic supported fields. After the first request, reveal date/term, recurrence, make-up, and assignment options only when relevant. On a conflict, reveal the specific rule and alternatives instead of a wall of policy text. Before confirmation, disclose all affected records and any limitations. After success, offer audit details and the related screen. Email, Google/Outlook, mobile notifications, and advanced workload optimization remain explicitly marked as future integrations.

## 9. Example Transcripts

### Happy path

User: “Lập lịch PLC cho lớp Điện công nghiệp K24 vào thứ Tư tuần sau, buổi sáng, học hàng tuần trong ba tuần.”

Assistant: “Mình hiểu là 11, 18 và 25/11/2026, 07:30–11:30. Dùng giáo viên Nguyễn Minh Anh và phòng A.203 theo hồ sơ lớp hiện tại?”

User: “Đúng.”

Assistant: “Đã kiểm tra cả ba ngày. Không trùng giáo viên, lớp hoặc phòng. Bản nháp: 3 buổi PLC, học kỳ I, 07:30–11:30, Nguyễn Minh Anh, A.203. Tạo lịch?”

User: “Xác nhận.”

Assistant: “Đã tạo 3 buổi. Lịch đã làm mới; mã thao tác được ghi trong nhật ký quản trị.”

### Error recovery

User: “Xếp lịch CNC thứ Sáu lúc 08:00, tuần này.”

Assistant: “Tuần này là 02–08/11/2026. Lịch hiện có của Lê Thu Hà dùng xưởng C.01 từ 08:00–11:00 ngày 06/11. Bạn muốn chọn phòng khác hay khung giờ khác?”

User: “Dời sang 11:30.”

Assistant: “Đã sửa bản nháp thành 11:30–14:30. Mình kiểm tra lại giáo viên, lớp và phòng: không còn xung đột. Tạo lịch này?”

User: “Có.”

Assistant: “Đã lưu lịch ngày 06/11 lúc 11:30. Mình không thay đổi lịch cũ.”

### Edge case: series with one exception

User: “Tạo ca trực phòng máy thứ Hai hàng tuần từ 09/11 đến 07/12, cần hai người, nhưng nghỉ tuần 23/11.”

Assistant: “Mình tạo các ngày 09/11, 16/11, 30/11 và 07/12; bỏ 23/11. Bạn muốn hệ thống đề xuất người rảnh cho từng ca hay đã có danh sách?”

User: “Đề xuất người rảnh.”

Assistant: “Mình chỉ thấy một người phù hợp cho ngày 30/11, trong khi mỗi ca cần hai người. Các ngày còn lại có ít nhất hai người. Bạn muốn lưu các ngày đủ người trước, hay chỉnh thời gian/phòng để tìm người thứ hai?”

User: “Chưa lưu. Chuyển ngày 30/11 sang buổi chiều.”

Assistant: “Đã đổi riêng ngày 30/11 sang 13:00–17:00 và kiểm tra lại. Hiện đủ hai người cho cả bốn ngày. Đây là bản xem trước cuối; xác nhận tạo bốn ca?”

User: “Xác nhận.”

Assistant: “Đã tạo bốn ca trực. Ngày 23/11 không được tạo; các phân công và người thực hiện đã ghi trong nhật ký.”