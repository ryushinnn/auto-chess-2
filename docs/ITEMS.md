# Trang bị

Sinh tự động bởi `py Tools/items/item_doc.py` (đừng sửa tay). Thiết kế và số liệu: `Tools/items/items.json` (menu *AutoChess/Items/Apply Item Config* ghi vào `Data/Items`), logic nội tại: `Assets/_main/Scripts/Core/Combat/ItemEffects.cs`. Mỗi tướng mang tối đa 3 trang bị; trang bị hoàn chỉnh = chỉ số của 2 nguyên liệu + nội tại.

## Nguyên liệu

| Nguyên liệu | Chỉ số |
|---|---|
| Kiếm (`_pdmg`) | +15% Sát thương vật lý |
| Thư (`_mdmg`) | +15% Sát thương phép |
| Tiễn (`_atkspd`) | +12% Tốc độ đánh |
| Nhẫn (`_crit`) | +12% Tỉ lệ chí mạng |
| Liên (`_energy`) | +25% Hiệu quả hồi năng lượng |
| Hoàn (`_maxhp`) | +180 Máu |
| Giáp (`_armor`) | +18 Giáp |
| Y (`_resistance`) | +18 Kháng phép |

## Trang bị hoàn chỉnh

### Thích Lân Giáp (`armor+armor`)

- Công thức: Giáp + Giáp
- Chỉ số: +36 Giáp
- Hợp với: tướng đỡ đòn
- Nội tại 1: Đòn chí mạng lên người mang chỉ gây sát thương thường.
- Nội tại 2: Khi trúng đòn đánh, gây 50 sát thương phép lên kẻ tấn công (tối đa 1 lần mỗi 1s).

### Thái Dương Thuẫn (`armor+atkspd`)

- Công thức: Giáp + Tiễn
- Chỉ số: +18 Giáp, +12% Tốc độ đánh
- Hợp với: mọi tướng
- Nội tại: Mỗi lần đánh hoặc trúng đòn: tăng 1.2% sát thương gây ra (tối đa 20 cộng dồn). Khi đủ cộng dồn, nhận thêm 20 Giáp / Kháng phép.

### Ngạo Vương Miện (`armor+crit`)

- Công thức: Giáp + Nhẫn
- Chỉ số: +18 Giáp, +12% Tỉ lệ chí mạng, +20% Sát thương chí mạng
- Hợp với: tướng sát thương vật lý
- Nội tại: Mỗi khi hạ gục kẻ địch, nhận [Khiên] bằng 40% Máu tối đa trong 5s.

### Chân Băng Giáp (`armor+energy`)

- Công thức: Giáp + Liên
- Chỉ số: +18 Giáp, +25% Hiệu quả hồi năng lượng
- Hợp với: tướng đỡ đòn
- Nội tại: Kẻ địch trong phạm vi 2 ô bị giảm 22% Tốc độ đánh.

### Mộc Linh Giáp (`armor+maxhp`)

- Công thức: Giáp + Hoàn
- Chỉ số: +18 Giáp, +180 Máu
- Hợp với: tướng đỡ đòn
- Nội tại: Mỗi 2s thiêu đốt kẻ địch trong phạm vi 1 ô: gây sát thương phép bằng 1.7% Máu tối đa của chúng và giảm hồi máu trong 3s.

### Oán Linh Thuẫn (`armor+mdmg`)

- Công thức: Giáp + Thư
- Chỉ số: +18 Giáp, +15% Sát thương phép
- Hợp với: tướng sát thương phép
- Nội tại: Đầu trận nhận [Khiên] bằng 30% Máu tối đa trong 8s.

### Chân Băng Quyền (`armor+pdmg`)

- Công thức: Giáp + Kiếm
- Chỉ số: +18 Giáp, +15% Sát thương vật lý
- Hợp với: tướng sát thương vật lý
- Nội tại: Lần đầu máu xuống dưới 60%: [Vô địch] 1s, sau đó tăng 35% Tốc độ đánh trong 6s.

### Xích Viêm Y (`armor+resistance`)

- Công thức: Giáp + Y
- Chỉ số: +18 Giáp, +18 Kháng phép
- Hợp với: tướng đỡ đòn
- Nội tại: Nhận 12 Giáp / Kháng phép cho mỗi kẻ địch đang nhắm vào người mang (tối đa 5).

### Đoạn Vân Tiễn (`atkspd+atkspd`)

- Công thức: Tiễn + Tiễn
- Chỉ số: +24% Tốc độ đánh
- Hợp với: tướng đánh thường nhiều
- Nội tại: Tăng 1 ô tầm đánh.

### Bạo Lôi Đao (`atkspd+crit`)

- Công thức: Tiễn + Nhẫn
- Chỉ số: +12% Tốc độ đánh, +22% Tỉ lệ chí mạng
- Hợp với: tướng sát thương vật lý
- Nội tại: Đòn đánh chí mạng giảm 50% Giáp của mục tiêu trong 3s.

### Lưu Quang Cung (`atkspd+energy`)

- Công thức: Tiễn + Liên
- Chỉ số: +12% Tốc độ đánh, +25% Hiệu quả hồi năng lượng
- Hợp với: tướng đánh thường nhiều
- Nội tại: Mỗi đòn đánh thứ 3 phóng điện, gây 65 sát thương phép lên tối đa 3 kẻ địch gần mục tiêu.

### Tử Huyết Nỗ (`atkspd+maxhp`)

- Công thức: Tiễn + Hoàn
- Chỉ số: +12% Tốc độ đánh, +180 Máu
- Hợp với: tướng sát thương phép
- Nội tại: Sau khi dùng kỹ năng, tăng 45% Tốc độ đánh trong 5s.

### Mãng Xà Cung (`atkspd+mdmg`)

- Công thức: Tiễn + Thư
- Chỉ số: +12% Tốc độ đánh, +15% Sát thương phép
- Hợp với: tướng đánh thường nhiều
- Nội tại: Mỗi đòn đánh tăng 4% Tốc độ đánh đến hết trận.

### Cuồng Phong Đao (`atkspd+pdmg`)

- Công thức: Tiễn + Kiếm
- Chỉ số: +12% Tốc độ đánh, +15% Sát thương vật lý
- Hợp với: tướng sát thương vật lý
- Nội tại: Gây thêm 25% sát thương lên mục tiêu có Máu tối đa cao hơn người mang.

### Ám Nguyệt Cung (`atkspd+resistance`)

- Công thức: Tiễn + Y
- Chỉ số: +12% Tốc độ đánh, +18 Kháng phép
- Hợp với: tướng đánh thường nhiều
- Nội tại: Đòn đánh bắn thêm một mũi vào kẻ địch gần mục tiêu nhất, gây 35% sát thương của đòn đánh.

### Bạo Lôi Trượng (`crit+crit`)

- Công thức: Nhẫn + Nhẫn
- Chỉ số: +24% Tỉ lệ chí mạng, +20% Sát thương chí mạng
- Hợp với: tướng sát thương vật lý
- Nội tại: Mỗi lần chí mạng tăng 4% Tốc độ đánh (tối đa 10 cộng dồn).

### Đoạt Hồn Liêm (`crit+energy`)

- Công thức: Nhẫn + Liên
- Chỉ số: +12% Tỉ lệ chí mạng, +25% Hiệu quả hồi năng lượng
- Hợp với: mọi tướng
- Nội tại: Hồi máu bằng 15% mọi sát thương gây ra; gấp đôi khi máu dưới 50%.

### Vong Thống Diện (`crit+maxhp`)

- Công thức: Nhẫn + Hoàn
- Chỉ số: +12% Tỉ lệ chí mạng, +180 Máu
- Hợp với: tướng đỡ đòn
- Nội tại: Giảm 22% sát thương phải nhận khi máu trên 40%.

### Cổ Nguyền Thư (`crit+mdmg`)

- Công thức: Nhẫn + Thư
- Chỉ số: +27% Tỉ lệ chí mạng, +15% Sát thương phép, +30% Sát thương chí mạng
- Hợp với: tướng sát thương phép
- Nội tại 1: Kỹ năng có thể chí mạng.
- Nội tại 2: Mỗi khi hạ gục kẻ địch, hồi 20 năng lượng.

### Bạo Lôi Phủ (`crit+pdmg`)

- Công thức: Nhẫn + Kiếm
- Chỉ số: +12% Tỉ lệ chí mạng, +15% Sát thương vật lý, +15% Sát thương chí mạng
- Hợp với: tướng sát thương vật lý
- Nội tại: Đòn đánh chí mạng gây thêm sát thương chuẩn bằng 35% sát thương của đòn đó.

### Ma Vương Miện (`crit+resistance`)

- Công thức: Nhẫn + Y
- Chỉ số: +12% Tỉ lệ chí mạng, +18 Kháng phép
- Hợp với: mọi tướng
- Nội tại 1: Miễn nhiễm khống chế trong 12s đầu trận.
- Nội tại 2: Trong thời gian đó, mỗi giây tăng 3% Tốc độ đánh (giữ đến hết trận).

### Thời Không Bảo (`energy+energy`)

- Công thức: Liên + Liên
- Chỉ số: +50% Hiệu quả hồi năng lượng, +15% Sát thương phép
- Hợp với: tướng sát thương phép
- Nội tại: Sau khi dùng kỹ năng, hồi 50 năng lượng.

### Tịnh Hồn Giáp (`energy+maxhp`)

- Công thức: Liên + Hoàn
- Chỉ số: +25% Hiệu quả hồi năng lượng, +180 Máu
- Hợp với: tướng đỡ đòn
- Nội tại: Mỗi 5s hồi cho đồng minh trong phạm vi 2 ô (kể cả bản thân) 9% máu đã mất.

### Tịnh Hồn Trượng (`energy+mdmg`)

- Công thức: Liên + Thư
- Chỉ số: +25% Hiệu quả hồi năng lượng, +15% Sát thương phép
- Hợp với: tướng sát thương phép
- Nội tại: Mỗi 3s tăng 6% Sát thương phép (cộng dồn đến hết trận).

### Tịnh Hồn Kiếm (`energy+pdmg`)

- Công thức: Liên + Kiếm
- Chỉ số: +25% Hiệu quả hồi năng lượng, +35% Sát thương vật lý
- Hợp với: tướng sát thương vật lý
- Nội tại: Mỗi đòn đánh hồi thêm 10 năng lượng.

### Vệ Vương Miện (`energy+resistance`)

- Công thức: Liên + Y
- Chỉ số: +25% Hiệu quả hồi năng lượng, +18 Kháng phép, +15% Sát thương phép
- Hợp với: tướng sát thương phép
- Nội tại: Đầu trận nhận 40 năng lượng; sau đó mỗi 2s hồi 15 năng lượng.

### Bất Tử Thuẫn (`maxhp+maxhp`)

- Công thức: Hoàn + Hoàn
- Chỉ số: +360 Máu
- Hợp với: tướng đỡ đòn
- Nội tại: Mỗi giây hồi 2.5% máu đã mất.

### Lưu Quang Trượng (`maxhp+mdmg`)

- Công thức: Hoàn + Thư
- Chỉ số: +180 Máu, +15% Sát thương phép
- Hợp với: tướng sát thương phép
- Nội tại: Sát thương kỹ năng thiêu đốt mục tiêu trong 5s: mỗi giây gây sát thương chuẩn bằng 1% Máu tối đa của mục tiêu, đồng thời giảm hồi máu.

### Huyết Cuồng Chuỳ (`maxhp+pdmg`)

- Công thức: Hoàn + Kiếm
- Chỉ số: +180 Máu, +15% Sát thương vật lý
- Hợp với: tướng sát thương vật lý
- Nội tại: Lần đầu máu xuống dưới 60%: nhận [Khiên] bằng 25% Máu tối đa và tăng 25% Sát thương vật lý trong 8s.

### Thiên Âm Bảo (`maxhp+resistance`)

- Công thức: Hoàn + Y
- Chỉ số: +180 Máu, +18 Kháng phép
- Hợp với: tướng đỡ đòn
- Nội tại: Kẻ địch trong phạm vi 2 ô bị giảm 40% Giáp.

### Ma Diệm Khôi (`mdmg+mdmg`)

- Công thức: Thư + Thư
- Chỉ số: +30% Sát thương phép
- Hợp với: tướng sát thương phép
- Nội tại: Tăng 15% sát thương gây ra.

### Ám Nguyệt Liêm (`mdmg+pdmg`)

- Công thức: Thư + Kiếm
- Chỉ số: +15% Sát thương phép, +15% Sát thương vật lý
- Hợp với: mọi tướng
- Nội tại: Hồi máu cho đồng minh thấp máu nhất bằng 18% sát thương người mang gây ra.

### Nhật Nguyệt Nhẫn (`mdmg+resistance`)

- Công thức: Thư + Y
- Chỉ số: +15% Sát thương phép, +18 Kháng phép
- Hợp với: tướng đỡ đòn
- Nội tại: Kẻ địch trong phạm vi 2 ô bị giảm 50% Kháng phép.

### Bá Vương Thương (`pdmg+pdmg`)

- Công thức: Kiếm + Kiếm
- Chỉ số: +30% Sát thương vật lý
- Hợp với: tướng sát thương vật lý
- Nội tại: Tăng 15% sát thương gây ra.

### Bạch Cốt Chuỳ (`pdmg+resistance`)

- Công thức: Kiếm + Y
- Chỉ số: +15% Sát thương vật lý, +18 Kháng phép, +15% Hút máu
- Hợp với: tướng sát thương vật lý
- Nội tại: Lần đầu máu xuống dưới 40%: nhận [Khiên] bằng 20% Máu tối đa trong 5s.

### Kháng Ma Thuẫn (`resistance+resistance`)

- Công thức: Y + Y
- Chỉ số: +36 Kháng phép
- Hợp với: tướng đỡ đòn
- Nội tại: Mỗi 2s hồi 2.5% Máu tối đa.
