# Consent notices: draft v1 (English and Hindi)

**Milestone:** M0 Discovery & legal, issue [#13](https://github.com/sagarworlds/Online-Exam-Platform/issues/13).
**Status:** draft for counsel review and native Hindi review. Not for publication until counsel signs it off. Text in square brackets is a decision still to be made.
**Seeds:** each notice is one `NoticeVersion` (`v1`) in the consent ledger (FR-44). The ledger stores a link, not the text, so these need hosting at real addresses before `ConsentSeeder` points at them. The seeder currently uses `https://example.invalid/legal/*-v1`.
**Purposes:** the three values of `ConsentPurpose`: `TermsOfService`, `PrivacyNotice`, `ProctoringDataProcessing`.

The Hindi wording reuses the terms already in `messages.hi.ts` (सहमति, गोपनीयता सूचना, सेवा की शर्तें, प्रॉक्टरिंग डेटा).

---

## 1. Terms of Service (`TermsOfService`, v1)

### English

These terms apply to everyone who uses the platform: candidates, guardians and staff.

- **Give true details.** Enter accurate information when you sign up. Your account is for you alone. Do not share your sign-in code with anyone.
- **Keep exam content private.** Do not copy, record or share exam questions. Do not get help during an exam unless the exam says that help is allowed.
- **Breaches.** If we find that these terms have been broken, we may warn you, review your attempt, or cancel it. You can ask for a review of a decision through a dispute or an issue report.
- **Minors.** A candidate under 18 uses the platform with the consent of a parent or guardian.
- **Questions.** Contact [grievance officer name, e-mail and postal address].

### हिन्दी

ये शर्तें हर उस व्यक्ति पर लागू होती हैं जो इस प्लेटफ़ॉर्म का उपयोग करता है: परीक्षार्थी, अभिभावक और कर्मचारी।

- **सही जानकारी दें।** साइन-अप करते समय सही जानकारी दें। आपका खाता केवल आपके लिए है। अपना साइन-इन कोड किसी के साथ साझा न करें।
- **परीक्षा की सामग्री गोपनीय रखें।** परीक्षा के प्रश्न न कॉपी करें, न रिकॉर्ड करें, न साझा करें। परीक्षा में सहायता तभी लें जब परीक्षा इसकी अनुमति दे।
- **उल्लंघन।** यदि हमें पता चले कि इन शर्तों का उल्लंघन हुआ है, तो हम आपको चेतावनी दे सकते हैं, आपके प्रयास की समीक्षा कर सकते हैं, या उसे रद्द कर सकते हैं। किसी निर्णय की समीक्षा के लिए आप डिस्प्यूट या समस्या रिपोर्ट के ज़रिए अनुरोध कर सकते हैं।
- **18 वर्ष से कम आयु।** 18 वर्ष से कम आयु का परीक्षार्थी प्लेटफ़ॉर्म का उपयोग माता-पिता या अभिभावक की सहमति से करता है।
- **प्रश्न।** संपर्क करें: [शिकायत अधिकारी का नाम, ई-मेल और डाक का पता]।

---

## 2. Privacy Notice (`PrivacyNotice`, v1)

### English

- **What we collect.** Your name, your e-mail address or phone number, your date of birth, the exams you take and your answers, the times you sign in and take an exam, and any messages you send us through the platform.
- **Why.** To run your account and sign-in, to run exams and send results, to send the e-mails and WhatsApp messages you have asked for, to keep an audit record, and to answer complaints.
- **Children.** If you are under 18, a parent or guardian must agree on your behalf. We do not show advertising to children, do not use third-party trackers in the exam, and do not build profiles of children.
- **Who handles it.** Our hosting and database providers, our e-mail provider, and WhatsApp (Meta), which sends sign-in codes and invitations. They act only on our instructions. [List of processors and where they hold data: to be confirmed, see the data map, section 8.]
- **How long we keep it.** [Retention periods for each kind of data, to be set by counsel.]
- **Your rights.** You can ask for a copy of your data, ask us to correct it or erase it, and make a complaint. Contact [grievance officer].
- **Withdrawing.** You can withdraw your agreement on the Consent page. [What happens to access to exams after withdrawal, to be set by counsel.]

### हिन्दी

- **हम क्या एकत्र करते हैं।** आपका नाम, आपका ई-मेल पता या फ़ोन नंबर, जन्मतिथि, आप जो परीक्षाएँ देते हैं और उनके उत्तर, साइन-इन और परीक्षा देने का समय, और प्लेटफ़ॉर्म के ज़रिए आप जो संदेश हमें भेजते हैं।
- **क्यों।** आपका खाता और साइन-इन चलाने के लिए, परीक्षा चलाने और परिणाम भेजने के लिए, आपके माँगे गए ई-मेल और व्हाट्सऐप संदेश भेजने के लिए, ऑडिट रिकॉर्ड रखने के लिए, और शिकायतों का उत्तर देने के लिए।
- **बच्चे।** यदि आप 18 वर्ष से कम आयु के हैं, तो आपकी ओर से माता-पिता या अभिभावक सहमति दें। हम बच्चों को विज्ञापन नहीं दिखाते, परीक्षा में तीसरे पक्ष के ट्रैकर नहीं लगाते, और बच्चों की प्रोफ़ाइलिंग नहीं करते।
- **इसे कौन संभालता है।** हमारे होस्टिंग और डेटाबेस प्रदाता, हमारा ई-मेल प्रदाता, और व्हाट्सऐप (Meta), जो साइन-इन कोड और आमंत्रण भेजता है। वे केवल हमारे निर्देश पर काम करते हैं। [प्रोसेसर की सूची और वे डेटा कहाँ रखते हैं: पुष्टि बाकी है, डेटा मैप का खंड 8 देखें।]
- **हम इसे कितने समय तक रखते हैं।** [हर प्रकार के डेटा की अवधारण अवधि, काउंसल तय करेंगे।]
- **आपके अधिकार।** आप अपने डेटा की प्रति माँग सकते हैं, उसे सुधरवा या मिटवा सकते हैं, और शिकायत कर सकते हैं। संपर्क करें: [शिकायत अधिकारी]।
- **सहमति वापस लेना।** आप सहमति 'सहमति' पेज पर वापस ले सकते हैं। [सहमति वापस लेने के बाद परीक्षा तक पहुँच पर क्या असर होगा, काउंसल तय करेंगे।]

---

## 3. Proctoring Data Processing (`ProctoringDataProcessing`, v1)

**Do not show this notice to any candidate yet.** Camera and screen features are milestone M6 and are not built. Under section 7.2 a candidate under 18 is not offered proctoring until counsel's opinion is recorded (issue #11). Counsel reviews this text before any camera or screen feature goes live.

### English

- **When it applies.** Only to an exam that uses proctoring, and only if you agree. The browser lock is enforced by the platform. Camera or screen capture happens only when the exam allows it.
- **What is recorded.** [Exact types of data, to be confirmed in M6.]
- **Who sees it.** [Reviewers, to be set by counsel.]
- **How long it is kept.** [Period, to be set by counsel.]
- **Decisions.** Flags raised during an exam are reviewed by a person. The platform makes no automatic decision about you from them.

### हिन्दी

- **कब लागू होता है।** केवल उस परीक्षा पर जिसमें प्रॉक्टरिंग हो, और केवल तब जब आप सहमति दें। ब्राउज़र लॉक प्लेटफ़ॉर्म लागू करता है। कैमरा या स्क्रीन रिकॉर्डिंग केवल तब होती है जब परीक्षा इसकी अनुमति दे।
- **क्या रिकॉर्ड होता है।** [डेटा के सटीक प्रकार, M6 में तय होंगे।]
- **इसे कौन देखता है।** [समीक्षक, काउंसल तय करेंगे।]
- **कितने समय तक रखा जाता है।** [अवधि, काउंसल तय करेंगे।]
- **निर्णय।** परीक्षा के दौरान उठे फ़्लैग एक व्यक्ति समीक्षा करता है। इनके आधार पर प्लेटफ़ॉर्म आपके बारे में कोई स्वचालित निर्णय नहीं लेता।

---

## Open before publication

1. Counsel review of all three notices (issue #11 covers the opinion on minors and proctoring).
2. Real hosting addresses for each notice, replacing `example.invalid`. Once a consent is recorded against a version, that version's text should not change, so publish any later change as `v2`. Decide the addresses before the first consent is recorded.
3. Native Hindi review of section 1 to 3.
4. Grievance officer name and address (also needed by the privacy notice).
5. Retention periods and the processor list, from the data map (#12).
