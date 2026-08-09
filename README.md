# OpenJPDF - PDF Editor

[![License](https://img.shields.io/badge/License-AGPLv3-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/Version-1.0.2-green.svg)](OpenJPDF.csproj)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-orange.svg)](OpenJPDF.csproj)

**โปรแกรมแก้ไข PDF ภาษาไทย ใช้งานฟรี เพื่อสังคม**

OpenJPDF เป็นโปรแกรมแก้ไข PDF แบบสมบูรณ์ พัฒนาขึ้นด้วย .NET 8 และ WPF ออกแบบมาเพื่อให้ประโยชน์กับสังคมโดยไม่มีค่าใช้จ่าย

**ใบอนุญาต:** GNU Affero General Public License v3 (AGPLv3) - See [LICENSE](LICENSE) file

---

## 📋 คุณสมบัติหลัก (Key Features)

### ✏️ การแก้ไขและใส่ข้อความ (Editing & Annotations)
- ✅ เปิดและดูไฟล์ PDF ได้รวดเร็ว
- ✅ เพิ่มข้อความ (Text Annotation) รองรับฟอนต์ไทย
- ✅ เพิ่มรูปภาพ (Image Annotation) ลงในเอกสาร
- ✅ เพิ่มรูปร่าง (Shape Annotation): สี่เหลี่ยม วงกลม เส้น
- ✅ แก้ไขและย้ายตำแหน่งออบเจ็กต์ได้ง่าย
- ✅ รองรับ OCR (Optical Character Recognition) ทั้งภาษาไทยและอังกฤษ

### 📑 เครื่องมือจัดการ PDF (PDF Tools)
- ✅ รวมไฟล์ PDF หลายไฟล์เข้าด้วยกัน (Merge PDFs)
- ✅ แยกหน้า PDF เป็นไฟล์แยก (Split Pages)
- ✅ ลบหน้าที่ไม่ต้องการออกจากเอกสาร
- ✅ ทำสำเนาหน้า (Duplicate Pages)
- ✅ หมุนหน้าเอกสารได้ (Rotate Pages)
- ✅ จัดเรียงลำดับหน้า (Reorder Pages)

### 🎨 คุณสมบัติอื่นๆ (Other Features)
- ✅ รองรับหลายเอกสารพร้อมกัน (Multi-tab)
- ✅ แสดงตัวอย่างหน้าเอกสาร (Page Thumbnails)
- ✅ โหมดการนำเสนอ (Presentation Mode) กด F5
- ✅ เพิ่มหัวกระดาษและท้ายกระดาษ (Header & Footer)
- ✅ ระบบ Undo/Redo
- ✅ รองรับการเปิดไฟล์ด้วย Drag & Drop
- ✅ รองรับภาษาไทยเต็มรูปแบบ

---

## 🚀 วิธีติดตั้ง (Installation)

### ดาวน์โหลดโปรแกรม (Download)
1. ไปที่หน้า [Releases](../../releases)
2. ดาวน์โหลดไฟล์ติดตั้ง `OpenJPDF-Setup-1.0.2.exe`
3. รันไฟล์ติดตั้งและทำตามขั้นตอน

### ความต้องการระบบ (System Requirements)
- **ระบบปฏิบัติการ**: Windows 10 ขึ้นไป
- **เฟรมเวิร์ก**: .NET 8.0 Runtime
- **หน่วยความจำ**: RAM ขั้นต่ำ 2 GB (แนะนำ 4 GB)
- **พื้นที่จัดเก็บ**: 100 MB

---

## 📖 วิธีใช้งาน (Usage Guide)

### เปิดไฟล์ PDF (Open PDF)
- **วิธีที่ 1**: คลิกปุ่ม `Open` บน Toolbar หรือกด `Ctrl+O`
- **วิธีที่ 2**: ลากไฟล์ PDF มาวางในหน้าต่างโปรแกรม (Drag & Drop)

### เพิ่มข้อความ (Add Text)
1. คลิกปุ่ม `เพิ่มข้อความ` (Add Text) บน Toolbar
2. คลิกตำแหน่งที่ต้องการเพิ่มในหน้า PDF
3. พิมพ์ข้อความในกล่องแก้ไข
4. ปรับฟอนต์ ขนาด และสีได้ตามต้องการ

### เพิ่มรูปภาพ (Add Image)
1. คลิกปุ่ม `เพิ่มรูปภาพ` (Add Image) บน Toolbar
2. เลือกรูปภาพจากคอมพิวเตอร์
3. ปรับขนาดและตำแหน่งได้

### รวมไฟล์ PDF (Merge PDFs)
1. คลิก `Tools` → `รวม PDF` (Merge PDF)
2. เลือกไฟล์ PDF ที่ต้องการรวม (2 ไฟล์ขึ้นไป)
3. ตั้งชื่อและบันทึกไฟล์ที่รวมแล้ว

### แยกหน้า PDF (Split Pages)
1. เปิดไฟล์ PDF ที่ต้องการแยก
2. คลิก `Tools` → `แยกหน้า` (Split Pages)
3. เลือกโฟลเดอร์ที่ต้องการบันทึก
4. แต่ละหน้าจะถูกแยกเป็นไฟล์แยก

### หมุนหน้าเอกสาร (Rotate Pages)
- **หมุนหน้าปัจจุบัน**: คลิกปุ่ม `หมุนซ้าย` (Ctrl+L) หรือ `หมุนขวา` (Ctrl+R)
- **หมุนหลายหน้า**: เลือกหลายหน้า (Ctrl+คลิก) แล้วกดปุ่มหมุน

### ลบหน้าเอกสาร (Delete Pages)
1. เลือกหน้าที่ต้องการลบใน Thumbnail หรือกด Ctrl+คลิกเพื่อเลือกหลายหน้า
2. คลิกปุ่ม `ลบ` บน Toolbar หรือกด Delete
3. ยืนยันการลบ

### ทำ OCR (Text Recognition)
1. คลิกปุ่ม `OCR` บน Toolbar
2. เลือกพื้นที่ที่ต้องการดึงข้อความ
3. ข้อความจะถูกแปลงเป็นข้อความแก้ไขได้

### โหมดนำเสนอ (Presentation Mode)
- กดปุ่ม `F5` เพื่อเข้าสู่โหมดนำเสนอ
- กด `Escape` เพื่อออกจากโหมด

---

## ⌨️ ปุ่มลัด (Keyboard Shortcuts)

| ปุ่มลัด | ฟังก์ชัน |
|-----------|-----------|
| `Ctrl+O` | เปิดไฟล์ (Open) |
| `Ctrl+S` | บันทึก (Save) |
| `Ctrl+L` | หมุนหน้าซ้าย (Rotate Left) |
| `Ctrl+R` | หมุนหน้าขวา (Rotate Right) |
| `Ctrl+D` | ทำสำเนาหน้า (Duplicate Page) |
| `Ctrl+Z` | ย้อนกลับ (Undo) |
| `Ctrl+Y` | ทำซ้ำ (Redo) |
| `F5` | โหมดนำเสนอ (Presentation Mode) |
| `Delete` | ลบหน้าที่เลือก (Delete Pages) |
| `Esc` | ออกจากโหมดแก้ไข (Exit Edit Mode) |

---

## 🛠️ เทคโนโลยีที่ใช้ (Technologies Used)

- **.NET 8.0** - แพลตฟอร์มหลัก
- **WPF** - กราฟิก UI และ User Experience
- **iText 7** - จัดการไฟล์ PDF
- **PDFtoImage** - แปลง PDF เป็นรูปภาพ
- **Tesseract OCR** - การแปลงรูปภาพเป็นข้อความ
- **CommunityToolkit.Mvvm** - สถาปัตยกรรม MVVM

---

## 📦 การสร้างโปรเจกต์ (Building from Source)

### ข้อกำหนดเบื้องต้น (Prerequisites)
- Visual Studio 2022 หรือใหม่กว่า
- .NET 8.0 SDK
- Windows 10 หรือใหม่กว่า

### ขั้นตอนการสร้าง (Build Steps)
```bash
# Clone repository
git clone https://github.com/yourusername/OpenJPDF.git
cd OpenJPDF

# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run application
dotnet run
```

---

## 📝 เวอร์ชันและประวัติ (Version History)

### รุ่น 1.0.2 (Current)
- แก้ไขปัญหา Save ซ้ำหลังจาก Save ครั้งแรก
- เพิ่ม Import PDF Pages เข้าเอกสารปัจจุบัน
- แก้ไขการย้ายหลายหน้าใน sidebar ให้ย้ายครบทั้งก้อน

### รุ่น 1.0.1
- เพิ่มฟีเจอร์ Merge/Split PDF
- ปรับปรุงประสิทธิภาพ
- แก้ไข Bug เล็กน้อย

### รุ่น 1.0.0
- เปิดตัวครั้งแรก
- ฟีเจอร์พื้นฐานสำหรับแก้ไข PDF

---

## 🤝 การมีส่วนร่วม (Contributing)

โปรแกรมนี้พัฒนาขึ้นเพื่อให้ประโยชน์กับสังคมโดยไม่มีค่าใช้จ่าย

หากคุณต้องการ:
- 🐛 รายงาน Bug
- 💡 แนะนำฟีเจอร์ใหม่
- 📚 ปรับปรุงเอกสาร
- 🔧 แก้ไขปัญหา

กรุณาติดต่อผู้พัฒนาหรือสร้าง Issue ใน repository

---

## ⚖️ ใบอนุญาต (License)

OpenJPDF เป็นซอฟต์แวร์เสรี (Free Software) ที่ได้รับอนุญาตภายใต้ **GNU Affero General Public License v3 (AGPLv3)**

**SPDX Identifier:** `AGPL-3.0-or-later`

**สิ่งที่อนุญาต:**
- ✅ ใช้งานฟรีทั้งในงานส่วนตัวและองค์กร
- ✅ ดัดแปลงและแจกจ่ายได้ (ต้องสมปรชาญาณภายใต้ AGPLv3)
- ✅ ใช้งานในการศึกษาและการกุศล
- ✅ ใช้งานเชิงพาณิชย์

**ข้อจำกัด:**
- ⚠️ การแก้ไขและการแจกจ่าย ต้องระบุให้เห็นชัดเจน
- ⚠️ ผู้แก้ไขต้องเปิดเผยโค้ด (Source Code Disclosure) - AGPLv3 requirement
- ⚠️ ต้องรักษาข้อความลิขสิทธิ์ (Copyright Notice)

**ข้อมูลตัวอักษร:**
- **ผู้พัฒนา**: สิทธิชาติ โปธิสิงห์ (Sittichat Pothising)
- **ปีที่ลงทะเบียน**: 2026
- **ธรรมชาติ**: ซอฟต์แวร์เสรี (Free Software) - พัฒนาเพื่อสังคม

**อ่านเพิ่มเติม:**
- [ไฟล์ LICENSE](LICENSE) - ข้อความสมบูรณ์ของ AGPLv3
- [LICENSE-FONTS.md](LICENSE-FONTS.md) - ใบอนุญาตของไฟล์ฟอนต์
- [GNU AGPLv3 Official](https://www.gnu.org/licenses/agpl-3.0.html) - ข้อมูลอย่างเป็นทางการ

**Third-party Libraries:**
- iText 7 (AGPLv3)
- PDFtoImage (MIT)
- CommunityToolkit.Mvvm (MIT)
- Tesseract OCR (Apache 2.0)

---

## 👨‍💻 ผู้พัฒนา (Developer)

**สิทธิชาติ โปธิสิงห์ (Sittichat Pothising)**

> "พัฒนาขึ้นเพื่อให้ประโยชน์แก่สังคมโดยไม่มีค่าใช้จ่าย"

---

## 🙏 ขอบคุณ (Acknowledgments)

โปรแกรมนี้ใช้ไลบรารีและเครื่องมือจากโอเพนซอร์สที่ยอดเยี่ยม:
- [iText 7](https://itextpdf.com/) - PDF Library
- [PDFtoImage](https://github.com/smith-timmermans/PDFtoImage) - PDF to Image Converter
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) - MVVM Framework
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) - OCR Engine

---

## 📧 ติดต่อ (Contact)

หากมีข้อสงสัยหรือต้องการติดต่อ สามารถทักทายได้ผ่าน:
- GitHub Issues: [สร้าง Issue](../../issues)
- Repository: [OpenJPDF](https://github.com/yourusername/OpenJPDF)

---

## ⭐ ถ้าชอบโปรแกรมนี้

ถ้าโปรแกรมนี้มีประโยชน์กับคุณ ขอเชิญ:
- ⭐ ให้ดาวที่ repository นี้
- 📢 แชร์ให้เพื่อนๆ ใช้งาน
- 🐛 รายงานปัญหาที่พบเพื่อปรับปรุง

---

**ขอบคุณที่ใช้งาน OpenJPDF! 🙏**

---

# OpenJPDF - PDF Editor (English)

[![License](https://img.shields.io/badge/License-Free%20for%20Society-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/Version-1.0.2-green.svg)](OpenJPDF.csproj)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-orange.svg)](OpenJPDF.csproj)

**A Thai-language PDF editor - Free for Society**

OpenJPDF is a comprehensive PDF editor built with .NET 8 and WPF, designed to benefit society at no cost.

---

## 📋 Key Features

### ✏️ Editing & Annotations
- ✅ Quick PDF viewing and opening
- ✅ Add text annotations with Thai font support
- ✅ Add image annotations to documents
- ✅ Add shape annotations: rectangles, circles, lines
- ✅ Easy object editing and positioning
- ✅ OCR (Optical Character Recognition) for Thai and English

### 📑 PDF Tools
- ✅ Merge multiple PDF files together (Merge PDFs)
- ✅ Split PDF pages into separate files (Split Pages)
- ✅ Delete unwanted pages from documents
- ✅ Duplicate pages
- ✅ Rotate document pages
- ✅ Reorder page sequence

### 🎨 Other Features
- ✅ Multi-document support (Multi-tab)
- ✅ Page thumbnail previews
- ✅ Presentation mode (Press F5)
- ✅ Add headers and footers
- ✅ Undo/Redo system
- ✅ Drag & Drop file opening
- ✅ Full Thai language support

---

## 🚀 Installation

### Download Program
1. Go to [Releases](../../releases) page
2. Download the installer file `OpenJPDF-Setup-1.0.2.exe`
3. Run the installer and follow the steps

### System Requirements
- **Operating System**: Windows 10 or higher
- **Framework**: .NET 8.0 Runtime
- **Memory**: Minimum 2 GB RAM (Recommended 4 GB)
- **Storage**: 100 MB

---

## 📖 Usage Guide

### Open PDF
- **Method 1**: Click `Open` button on Toolbar or press `Ctrl+O`
- **Method 2**: Drag and drop PDF file into the program window

### Add Text
1. Click `Add Text` button on Toolbar
2. Click the position where you want to add text in the PDF page
3. Type text in the edit box
4. Adjust font, size, and color as needed

### Add Image
1. Click `Add Image` button on Toolbar
2. Select image from your computer
3. Adjust size and position

### Merge PDFs
1. Click `Tools` → `Merge PDF`
2. Select PDF files to merge (2 or more files)
3. Name and save the merged file

### Split Pages
1. Open the PDF file you want to split
2. Click `Tools` → `Split Pages`
3. Select the folder to save
4. Each page will be split into separate files

### Rotate Pages
- **Rotate current page**: Click `Rotate Left` (Ctrl+L) or `Rotate Right` (Ctrl+R)
- **Rotate multiple pages**: Select multiple pages (Ctrl+click) and click rotate button

### Delete Pages
1. Select pages to delete in Thumbnail or press Ctrl+click to select multiple pages
2. Click `Delete` button on Toolbar or press Delete
3. Confirm deletion

### OCR (Text Recognition)
1. Click `OCR` button on Toolbar
2. Select the area you want to extract text from
3. Text will be converted to editable text

### Presentation Mode
- Press `F5` to enter presentation mode
- Press `Escape` to exit the mode

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Function |
|----------|----------|
| `Ctrl+O` | Open File |
| `Ctrl+S` | Save |
| `Ctrl+L` | Rotate Left |
| `Ctrl+R` | Rotate Right |
| `Ctrl+D` | Duplicate Page |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `F5` | Presentation Mode |
| `Delete` | Delete Selected Pages |
| `Esc` | Exit Edit Mode |

---

## 🛠️ Technologies Used

- **.NET 8.0** - Main platform
- **WPF** - Graphics UI and User Experience
- **iText 7** - PDF file handling
- **PDFtoImage** - PDF to Image conversion
- **Tesseract OCR** - Image to text conversion
- **CommunityToolkit.Mvvm** - MVVM Architecture

---

## 📦 Building from Source

### Prerequisites
- Visual Studio 2022 or newer
- .NET 8.0 SDK
- Windows 10 or newer

### Build Steps
```bash
# Clone repository
git clone https://github.com/yourusername/OpenJPDF.git
cd OpenJPDF

# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run application
dotnet run
```

---

## 📝 Version History

### Version 1.0.2 (Current)
- Fixed repeat Save after the first Save
- Added Import PDF Pages into the current document
- Fixed sidebar multi-page move/reorder to move the full selected block

### Version 1.0.1
- Added Merge/Split PDF features
- Performance improvements
- Minor bug fixes

### Version 1.0.0
- Initial release
- Basic PDF editing features

---

## 🤝 Contributing

This program was developed to benefit society at no cost.

If you want to:
- 🐛 Report a Bug
- 💡 Suggest new features
- 📚 Improve documentation
- 🔧 Fix issues

Please contact the developer or create an Issue in the repository

---

## ⚖️ License

This software was developed to benefit society at no cost.

**Permissions:**
- ✅ Free use for both personal and organizational purposes
- ✅ Modification and distribution allowed
- ✅ Use in education and charity work

**Restrictions:**
- ❌ Cannot be sold directly for profit
- ❌ Must state that it was developed by Sittichat Pothising

For more details, see the [LICENSE](LICENSE) file

---

## 👨‍💻 Developer

**Sittichat Pothising (สิทธิชาติ โปธิสิงห์)**

> "Developed to benefit society at no cost"

---

## 🙏 Acknowledgments

This program uses excellent open-source libraries and tools:
- [iText 7](https://itextpdf.com/) - PDF Library
- [PDFtoImage](https://github.com/smith-timmermans/PDFtoImage) - PDF to Image Converter
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) - MVVM Framework
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) - OCR Engine

---

## 📧 Contact

If you have questions or want to get in touch, you can reach us via:
- GitHub Issues: [Create Issue](../../issues)
- Repository: [OpenJPDF](https://github.com/yourusername/OpenJPDF)

---

## ⭐ If You Like This Program

If this program is useful to you, please:
- ⭐ Star this repository
- 📢 Share with friends to use
- 🐛 Report issues found for improvement

---

**Thank you for using OpenJPDF! 🙏**
