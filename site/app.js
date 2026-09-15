const CMDS={win:'Invoke-WebRequest https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest/download/SafeDiskCleaner-latest-setup-win64.exe -OutFile $env:TEMP\SafeDiskSetup.exe',linux:'curl -L https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest/download/SafeDiskCleaner-latest-linux-x64.tar.gz -o SafeDiskCleaner.tar.gz',mac:'open https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest'};
function tab(el,os){document.querySelectorAll('.tabs button').forEach(b=>b.classList.remove('active'));el.classList.add('active');document.getElementById('cmd').textContent=CMDS[os];}
function copyCmd(){const t=document.getElementById('cmd').textContent;navigator.clipboard.writeText(t).then(()=>{const b=document.querySelector('.copy');const o=b.textContent;b.textContent='✅ Скопійовано!';setTimeout(()=>b.textContent=o,1800);});}
const io=new IntersectionObserver(es=>es.forEach(e=>{if(e.isIntersecting){e.target.classList.add('vis');io.unobserve(e.target);}}),{threshold:.12});
document.querySelectorAll('.reveal').forEach(el=>io.observe(el));
document.querySelectorAll('.shot').forEach(s=>s.addEventListener('click',()=>{const i=s.querySelector('img');document.getElementById('lbImg').src=i.src;document.getElementById('lbCap').textContent=s.querySelector('h3').textContent;document.getElementById('lightbox').classList.add('open');}));
document.addEventListener('keydown',e=>{if(e.key==='Escape')document.getElementById('lightbox').classList.remove('open');});
addEventListener('scroll',()=>document.getElementById('toTop').classList.toggle('show',scrollY>700));
document.getElementById('year').textContent=new Date().getFullYear();
// counters
const cio=new IntersectionObserver(es=>es.forEach(e=>{if(!e.isIntersecting)return;const b=e.target;const end=parseFloat(b.dataset.count);const suf=b.dataset.suffix||'';const t0=performance.now();const step=t=>{const p=Math.min(1,(t-t0)/1200);b.textContent=(end%1? (end*p).toFixed(1):Math.round(end*p))+suf;if(p<1)requestAnimationFrame(step)};requestAnimationFrame(step);cio.unobserve(b);}),{threshold:.5});
document.querySelectorAll('[data-count]').forEach(el=>cio.observe(el));
// img fallback
document.querySelectorAll('.shot img').forEach(img=>{img.loading='lazy';img.onerror=()=>{img.style.display='none';img.parentElement.innerHTML='<div style=\"padding:60px 20px;text-align:center;color:#5b6577;font-size:3rem\">🖼️</div>';};});

// Wiring for former inline handlers (CSP: no inline JS).
document.getElementById("burger")?.addEventListener("click", () => {
  document.querySelector(".nav-links")?.classList.toggle("open");
});
document.querySelectorAll(".tabs button[data-os]").forEach((b) => {
  b.addEventListener("click", () => tab(b, b.dataset.os));
});
document.getElementById("copyCmd")?.addEventListener("click", copyCmd);
document.getElementById("lightbox")?.addEventListener("click", function () {
  this.classList.remove("open");
});
document.getElementById("toTop")?.addEventListener("click", () => {
  scrollTo({ top: 0, behavior: "smooth" });
});
