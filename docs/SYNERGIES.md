# Synergies (realm / role)

Sinh tự động bởi `py Tools/balance/synergy_doc.py` từ `Assets/_main/Data/Destinies` (đừng sửa tay; số liệu chỉnh trong `Tools/balance/balance_config.json` rồi `balance.py apply`). Logic: `Assets/_main/Scripts/Core/Combat/TraitEffects.cs`.

Ký hiệu chỉ số: `hp` = Máu, `pdmg` = Sát thương vật lý, `mdmg` = Sát thương phép, `arm` = Giáp, `res` = Kháng phép, `aspd` = Tốc độ đánh, `crc` = Tỉ lệ chí mạng, `crd` = Sát thương chí mạng, `ppen` = Xuyên giáp, `mpen` = Xuyên kháng phép, `ls` = Hút máu, `ere` = Hiệu quả hồi năng lượng, `ten` = Kháng hiệu ứng.

## Realm

### Hỗn mang (Chaos)

Mốc kích hoạt: 2 tướng. Thành viên: A Chốc (Aatrox_Dark, Truyền thuyết), A Chốc (Aatrox_Light, Truyền thuyết).

Triệu hồi Chiến Binh Tận Thế tham chiến

Chỉ số của Chiến Binh Tận Thế được tính dựa trên chỉ số của A Chốc Nhật mạnh nhất và A Chốc Nguyệt mạnh nhất:
Tổng: Máu / Sát thương vật lý / Sát thương phép
Trung bình: Giáp / Kháng phép / Tỉ lệ chí mạng / Sát thương chí mạng
Lớn hơn: Xuyên giáp / Xuyên kháng phép

Ngoài ra, Chiến Binh Tận Thế nhận thêm 0 Máu. Mỗi 7 s, Chiến Binh Tận Thế hồi phục 8% Máu đã mất và nhận [Vô địch], miễn sát thương và khống chế, duy trì 0.8 s.

Ghi chú triển khai:

- Cần cả A Chốc Nhật (Aatrox_Light) và A Chốc Nguyệt (Aatrox_Dark) trên sân.

Tham số: `hp_bonus` = 0, `interval` = 7, `heal_by_hp_loss` = 8%, `duration` = 0.8.

### Thánh thần (Divine)

Mốc kích hoạt: 2 / 3 / 4 / 5 tướng. Thành viên: Ái Liên (Irelia, Vô danh), Dạ Xuân (Yasuo, Vô danh), Mạc Ngân (Morgana, Siêu việt).

Đòn đánh của **Thánh thần** có tỉ lệ gây [Choáng] trong 0.8 s. Đòn đánh chí mạng hoặc sát thương chuẩn có tỉ lệ gây [Choáng] gấp 2 lần và thời gian duy trì tăng 50%.
- Mốc 2 tướng: Tỉ lệ 12%
- Mốc 3 tướng: Tỉ lệ 16%
- Mốc 4 tướng: Tỉ lệ 20%. **Thánh thần** gây thêm 15% sát thương lên kẻ địch bị [Choáng]
- Mốc 5 tướng: Tỉ lệ 25%. **Thánh thần** gây thêm 30% sát thương lên kẻ địch bị [Choáng]

Ghi chú triển khai:

- Chỉ áp dụng cho đòn đánh thường.

Tham số: `stun_chance_0` = 12%, `stun_chance_1` = 16%, `stun_chance_2` = 20%, `stun_chance_3` = 25%, `stun_duration` = 0.8, `crit_stun_chance_mul` = 2, `crit_stun_duration_mul` = 50%, `dmg_bonus_0` = 15%, `dmg_bonus_1` = 30%.

### Cơ giới (Mecha)

Mốc kích hoạt: 2 / 3 / 4 / 5 tướng. Thành viên: Mạnh Phi (Malphite, Siêu việt), Rét (Zed, Truyền thuyết).

Với mỗi 400 sát thương gây ra hoặc 650 sát thương nhận vào, các **Cơ giới** gửi yêu cầu hỗ trợ tới PHI ĐỘI. Khi nhận được yêu cầu, PHI ĐỘI triệu gọi tên lửa tấn công kẻ địch thấp máu nhất.
- Mốc 2 tướng: Tên lửa gây 100 sát thương vật lý / sát thương phép (dựa vào Giáp / Kháng phép của mục tiêu)
- Mốc 3 tướng: Giảm 30% điều kiện gửi yêu cầu hỗ trợ
- Mốc 4 tướng: Tên lửa tăng 30% sát thương
- Mốc 5 tướng: Tên lửa gây sát thương chuẩn và có 33% Tỉ lệ chí mạng, gây 150% sát thương

Ghi chú triển khai:

- Mỗi Cơ giới tự đếm sát thương gây ra / nhận vào; mốc 4 tên lửa có thể chí mạng.
- Tên lửa gây sát thương vật lý hay phép tuỳ chỉ số phòng thủ nào của mục tiêu thấp hơn.

Tham số: `dmg_deal_threshold` = 400, `dmg_deal_threshold` = 650, `rocket_dmg` = 100, `requirement_reduce` = 30%, `rocket_dmg_bonus` = 30%, `rocket_crit_chance` = 33%, `rocket_crit_dmg` = 150%.

### Phàm nhân (Mortal)

Mốc kích hoạt: 1 / 2 / 4 / 6 tướng. Thành viên: Khánh Linh (Akali, Vô danh), Cát Linh (Caitlyn, Vô danh), Mỹ Phương (MissFortune, Vô danh), Trí Tâm (Tristana, Vô danh), Trinh (Jinx, Siêu việt), Cẩm Thanh (Katarina, Siêu việt).

Mỗi **Phàm nhân** tăng một loại chỉ số riêng biệt cho bản thân và các **Phàm nhân** khác. Nếu trong đội có nhiều **Phàm nhân**, lượng chỉ số được cường hoá.

Khánh Linh: 15 Sát thương phép
Trinh: 15 Sát thương vật lý
Cát Linh: 15% Hiệu quả hồi năng lượng
Cẩm Thanh: 8% Tỉ lệ chí mạng
Trí Tâm: 20% Sát thương chí mạng
Mỹ Phương: 8% Xuyên giáp / Xuyên kháng phép
- Mốc 1 tướng: Nhận 100% chỉ số
- Mốc 2 tướng: Nhận 125% chỉ số
- Mốc 4 tướng: Nhận 160% chỉ số
- Mốc 6 tướng: Nhận 200% chỉ số. Ngoài ra nhận thêm 20% Kháng hiệu ứng, 25 Giáp / Kháng phép

Ghi chú triển khai:

- Mỗi tướng chỉ tính một lần dù có nhiều bản sao trên sân.

Tham số: `mdmg` = 15, `pdmg` = 15, `ere` = 15%, `crc` = 8%, `crd` = 20%, `pen` = 8%, `attr_0` = 100%, `attr_1` = 125%, `attr_2` = 160%, `attr_3` = 200%, `tenacity_mul` = 20%, `def` = 25.

### Âm ti (Nether)

Mốc kích hoạt: 2 / 3 / 4 / 5 tướng. Thành viên: Ánh (Ashe, Vô danh), Tiên Mệnh (Teemo, Siêu việt), Du Thiên (Yone, Truyền thuyết).

**Âm ti** chuyển hoá một phần sát thương gây ra thành các cộng dồn [Nợ máu] giảm dần theo thời gian lên kẻ địch. Kẻ địch có máu thấp hơn số cộng dồn [Nợ máu] lập tức bị hành quyết và 35% số cộng dồn [Nợ máu] chuyển hoá thành máu hồi phục chia đều cho tất cả **Âm ti** còn sống.
- Mốc 2 tướng: Chuyển hoá 15%
- Mốc 3 tướng: Chuyển hoá 22%
- Mốc 4 tướng: Chuyển hoá 30%
- Mốc 5 tướng: Chuyển hoá 40%. Các cộng dồn [Nợ máu] không còn giảm dần theo thời gian

Ghi chú triển khai:

- Nợ máu giảm 15% mỗi giây (trừ ở mốc cuối).

Tham số: `dmg_to_debt_0` = 15%, `dmg_to_debt_1` = 22%, `dmg_to_debt_2` = 30%, `dmg_to_debt_3` = 40%, `heal_by_debt` = 35%.

## Role

### Thích khách (Assassin)

Mốc kích hoạt: 2 / 3 / 4 tướng. Thành viên: Khánh Linh (Akali, Vô danh), Cẩm Thanh (Katarina, Siêu việt), A Chốc (Aatrox_Dark, Truyền thuyết), Rét (Zed, Truyền thuyết).

**Thích khách** nhận thêm Tỉ lệ chí mạng. Đòn đánh chí mạng của **Thích khách** có tỉ lệ chuyển hoá thành sát thương chuẩn.
- Mốc 2 tướng: 15% Tỉ lệ chí mạng, tỉ lệ 15%
- Mốc 3 tướng: 30% Tỉ lệ chí mạng, tỉ lệ 25%
- Mốc 4 tướng: 50% Tỉ lệ chí mạng, tỉ lệ 35%

Ghi chú triển khai:

- Chỉ tính đòn chí mạng của chính Thích khách; sát thương chuẩn bỏ qua giáp / kháng phép.

Tham số: `crc_0` = 15%, `crc_1` = 30%, `crc_2` = 50%, `chance_0` = 15%, `chance_1` = 25%, `chance_2` = 35%.

### Quyền Vương (Bruiser)

Mốc kích hoạt: 2 / 3 / 4 tướng. Thành viên: Mạnh Phi (Malphite, Siêu việt).

**Quyền vương** nhận thêm Máu. Khi **Quyền vương** có nhiều hơn 70% Máu, nhận lượng lớn Giáp / Kháng phép.
- Mốc 2 tướng: 250 Máu, 25 Giáp / Kháng phép
- Mốc 3 tướng: 500 Máu, 50 Giáp / Kháng phép
- Mốc 4 tướng: 900 Máu, 90 Giáp / Kháng phép

Ghi chú triển khai:

- Giáp / kháng phép bật khi máu trên ngưỡng và tắt khi xuống dưới ngưỡng (bật lại nếu được hồi máu).

Tham số: `hp_threshold` = 70%, `hp_0` = 250, `hp_1` = 500, `hp_2` = 900, `def_0` = 25, `def_1` = 50, `def_2` = 90.

### Tín đồ (Cultist)

Mốc kích hoạt: 2 / 3 / 4 tướng. Thành viên: Tiên Mệnh (Teemo, Siêu việt), Du Thiên (Yone, Truyền thuyết).

**Tín đồ** mạnh nhất trở thành vật chủ cho CHÚA QUỶ, nhận lượng lớn chỉ số. Mỗi khi vật chủ tử vong, **Tín đồ** mạnh nhất còn sống được lựa chọn trở thành vật chủ mới, CHÚA QUỶ sẽ tái sinh ở bậc thấp hơn. (bậc thấp nhất không thể tiếp tục tái sinh)
- Mốc 2 tướng: 150 Máu, 20 Sát thương vật lý / Sát thương phép, 6% Hút máu
- Mốc 3 tướng: 350 Máu, 45 Sát thương vật lý / Sát thương phép, 10% Hút máu
- Mốc 4 tướng: 700 Máu, 90 Sát thương vật lý / Sát thương phép, 15% Hút máu

Ghi chú triển khai:

- Vật chủ = Tín đồ mạnh nhất (sao cao hơn, rồi danh tiếng cao hơn, rồi máu nhiều hơn).
- Ngoài chỉ số chung, CHÚA QUỶ nhận thêm lợi thế theo role còn lại của vật chủ: Pháp sư +40% hiệu quả hồi năng lượng và đầy năng lượng ngay; Kiếm sĩ +30% tốc độ đánh; Thích khách +40% sát thương chí mạng; Xạ thủ +25% tỉ lệ chí mạng; Quyền Vương +40 giáp / kháng phép; chỉ là Tín đồ: +20% máu.

Tham số: `hp_0` = 150, `hp_1` = 350, `hp_2` = 700, `dmg_0` = 20, `dmg_1` = 45, `dmg_2` = 90, `ls_0` = 6%, `ls_1` = 10%, `ls_2` = 15%.

### Kiếm sĩ (Duelist)

Mốc kích hoạt: 2 / 3 / 4 tướng. Thành viên: Ái Liên (Irelia, Vô danh), Dạ Xuân (Yasuo, Vô danh), A Chốc (Aatrox_Dark, Truyền thuyết), A Chốc (Aatrox_Light, Truyền thuyết), Du Thiên (Yone, Truyền thuyết).

**Kiếm sĩ** nhận thêm Sát thương vật lý. Khi **Kiếm sĩ** bị tấn công bởi đòn đánh, có tỉ lệ Né tránh, miễn sát thương từ đòn đánh đó (vô hiệu với đòn đánh chí mạng hoặc sát thương chuẩn)
- Mốc 2 tướng: 10 Sát thương vật lý, tỉ lệ 8%
- Mốc 3 tướng: 25 Sát thương vật lý, tỉ lệ 14%
- Mốc 4 tướng: 55 Sát thương vật lý, tỉ lệ 20%

Ghi chú triển khai:

- Tham số đầu (min_hp_threshold) hiện không dùng.

Tham số: `min_hp_threshold` = 30%, `pdmg_0` = 10, `pdmg_1` = 25, `pdmg_2` = 55, `dodge_0` = 8%, `dodge_1` = 14%, `dodge_2` = 20%.

### Xạ thủ (Marksman)

Mốc kích hoạt: 2 / 3 / 4 tướng. Thành viên: Ánh (Ashe, Vô danh), Cát Linh (Caitlyn, Vô danh), Mỹ Phương (MissFortune, Vô danh), Trí Tâm (Tristana, Vô danh), Trinh (Jinx, Siêu việt).

**Xạ thủ** nhận thêm Tốc độ đánh. Mỗi khi một **Xạ thủ** tử trận, tất cả **Xạ thủ** còn sống tiếp tục nhận lượng Tốc độ đánh đó trong một khoảng thời gian (có thể cộng dồn).
- Mốc 2 tướng: 8% Tốc độ đánh, duy trì 5 s
- Mốc 3 tướng: 20% Tốc độ đánh, duy trì 7 s
- Mốc 4 tướng: 40% Tốc độ đánh, duy trì 12 s

Ghi chú triển khai:

- Mỗi Xạ thủ tử trận cộng thêm một lượt tốc độ đánh riêng (cộng dồn được), hết hạn theo thời gian.

Tham số: `bonus_atk_spd_0` = 8%, `bonus_atk_spd_1` = 20%, `bonus_atk_spd_2` = 40%, `burst_duration_0` = 5, `burst_duration_1` = 7, `burst_duration_2` = 12.

### Pháp sư (Sorcerer)

Mốc kích hoạt: 2 / 3 / 4 tướng. Thành viên: Mạc Ngân (Morgana, Siêu việt), Tiên Mệnh (Teemo, Siêu việt), A Chốc (Aatrox_Light, Truyền thuyết).

**Pháp sư** nhận thêm Sát thương phép. Sau khi **Pháp sư** thi triển kĩ năng lần đầu, có tỉ lệ thi triển kĩ năng thêm một lần nữa.
- Mốc 2 tướng: 20 Sát thương phép, 25%
- Mốc 3 tướng: 45 Sát thương phép, 35%
- Mốc 4 tướng: 90 Sát thương phép, 45%

Ghi chú triển khai:

- Lần thi triển thêm: năng lượng được nạp đầy ngay sau lần thi triển đầu tiên.

Tham số: `mdmg_0` = 20, `mdmg_1` = 45, `mdmg_2` = 90, `chance_0` = 25%, `chance_1` = 35%, `chance_2` = 45%.
