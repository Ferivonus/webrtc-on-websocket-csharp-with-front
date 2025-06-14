console.log("webrtc.js yüklendi");

const signalingUrl = "ws://localhost:5000/ws/";
let ws, pc, localStream;
const candidateQueue = [];

const startBtn    = document.getElementById("startBtn");
const statusEl    = document.getElementById("status");
const localVideo  = document.getElementById("localVideo");
const remoteVideo = document.getElementById("remoteVideo");

startBtn.addEventListener("click", init);

async function init() {
  console.log("init() tetiklendi");
  statusEl.innerText = "WebSocket'e bağlanılıyor...";
  ws = new WebSocket(signalingUrl);

  ws.addEventListener("open", async () => {
    statusEl.innerText = "Medya aygıtlarına erişiliyor...";
    try {
      // Kullanıcı etkileşiminde izin isteği
      localStream = await navigator.mediaDevices.getUserMedia({
        audio: true, video: true
      });
      localVideo.srcObject = localStream;
      console.log("getUserMedia başarılı");
    } catch (err) {
      console.error("getUserMedia hatası:", err);
      statusEl.innerText = "Medya cihazlarına erişilemedi. Lütfen izin verin.";  // 📌
      return;
    }

    setupPeerConnection();

    // İlk teklif yalnızca bir taraf tarafından gönderilmeli
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    ws.send(JSON.stringify(offer));
    statusEl.innerText = "Teklif (offer) gönderildi.";
    console.log("Offer:", offer);
  });

  ws.addEventListener("message", async ({ data }) => {
    let msg;
    try { msg = JSON.parse(data); }
    catch { return; }

    if (msg.type === "offer" || msg.type === "answer") {
      // SDP uygulandıktan sonra bekleyen ICE adaylarını ekle
      await pc.setRemoteDescription(msg);
      console.log(msg.type, "uzak tanımlama ayarlandı");
      while (candidateQueue.length) {
        try {
          const c = candidateQueue.shift();
          await pc.addIceCandidate(c);
          console.log("Buffered ICE eklendi:", c);
        } catch (e) {
          console.error("Buffered ICE ekleme hatası:", e);
        }
      }
      if (msg.type === "offer") {
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        ws.send(JSON.stringify(answer));
        statusEl.innerText = "Answer gönderildi.";
      } else {
        statusEl.innerText = "Bağlantı kuruldu.";
      }
    }
    else if (msg.type === "candidate") {
      // Remote-desc yoksa kuyruğa al
      if (!pc.remoteDescription) {
        candidateQueue.push(msg.candidate);
        console.log("ICE aday kuyruğa alındı:", msg.candidate);
      } else {
        try {
          await pc.addIceCandidate(msg.candidate);
          console.log("ICE aday eklendi:", msg.candidate);
        } catch (e) {
          console.error("ICE ekleme hatası:", e);
        }
      }
    }
  });

  ws.addEventListener("error", e => {
    console.error("WebSocket hatası:", e);
    statusEl.innerText = "WebSocket bağlantı hatası.";
  });
  ws.addEventListener("close", e => {
    console.warn("WebSocket kapandı:", e);
    statusEl.innerText = "Sunucuya bağlantı koptu.";
  });
}

function setupPeerConnection() {
  pc = new RTCPeerConnection({
    iceServers: [{ urls: "stun:stun.l.google.com:19302" }]
  });

  pc.addEventListener("icecandidate", ({ candidate }) => {
    if (candidate && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: "candidate", candidate }));
      console.log("ICE candidate gönderildi:", candidate);
    }
  });

  pc.addEventListener("track", ({ streams: [stream] }) => {
    remoteVideo.srcObject = stream;
    console.log("Uzak akış oynatılıyor");
  });

  localStream.getTracks().forEach(track => {
    pc.addTrack(track, localStream);
    console.log("Yerel track eklendi:", track.kind);
  });
}
