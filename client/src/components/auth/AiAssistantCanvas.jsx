import React, { useEffect, useRef } from 'react';

function AiAssistantCanvas({ hideEyes = false }) {
  const canvasRef = useRef(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const W = 180;
    const H = 180;

    // Track mouse globally across the whole page
    const mouse = { x: W / 2, y: H / 2 };

    const handleMouseMove = (e) => {
      const rect = canvas.getBoundingClientRect();
      const canvasCenterX = rect.left + rect.width / 2;
      const canvasCenterY = rect.top + rect.height / 2;
      const dx = e.clientX - canvasCenterX;
      const dy = e.clientY - canvasCenterY;
      const dist = Math.sqrt(dx * dx + dy * dy) || 1;
      const maxTravel = 90;
      const factor = Math.min(dist, maxTravel) / dist;
      mouse.x = W / 2 + dx * factor;
      mouse.y = W / 2 + dy * factor;
    };

    window.addEventListener('mousemove', handleMouseMove);

    // Physics & animation states
    const eyeCurL = { x: 0, y: 0, vx: 0, vy: 0 };
    const eyeCurR = { x: 0, y: 0, vx: 0, vy: 0 };
    let blinkT = 0;
    let blinkNext = 120;
    let blinkDur = 0;
    let isBlinking = false;
    let antennaT = 0;
    let bodyBobT = 0;
    let earWiggleT = 0;
    let ledT = 0;
    let shieldAlpha = 0;

    const lerp = (a, b, t) => a + (b - a) * t;

    const getEyeTarget = (ex, ey) => {
      const dx = mouse.x - ex;
      const dy = mouse.y - ey;
      const dist = Math.sqrt(dx * dx + dy * dy) || 1;
      const maxR = 4.5;
      const r = Math.min(dist, maxR * 8) / (maxR * 8);
      return { x: (dx / dist) * r * maxR, y: (dy / dist) * r * maxR };
    };

    const drawRR = (x, y, w, h, r, fill, stroke, lw) => {
      ctx.beginPath();
      ctx.moveTo(x + r, y);
      ctx.lineTo(x + w - r, y);
      ctx.quadraticCurveTo(x + w, y, x + w, y + r);
      ctx.lineTo(x + w, y + h - r);
      ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
      ctx.lineTo(x + r, y + h);
      ctx.quadraticCurveTo(x, y + h, x, y + h - r);
      ctx.lineTo(x, y + r);
      ctx.quadraticCurveTo(x, y, x + r, y);
      ctx.closePath();
      if (fill) {
        ctx.fillStyle = fill;
        ctx.fill();
      }
      if (stroke) {
        ctx.strokeStyle = stroke;
        ctx.lineWidth = lw || 1.5;
        ctx.stroke();
      }
    };

    // Check prefers-reduced-motion
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    let animationId = null;

    const draw = () => {
      ctx.clearRect(0, 0, W, H);

      const shieldTarget = hideEyes ? 1 : 0;
      shieldAlpha = lerp(shieldAlpha, shieldTarget, 0.08);

      let bodyBob = 0;
      let antWave = 0;
      let earWig = 0;

      if (!prefersReducedMotion) {
        antennaT += 0.038;
        bodyBobT += 0.022;
        earWiggleT += 0.034;
        ledT += 0.02;

        bodyBob = Math.sin(bodyBobT) * 2.0 + Math.sin(bodyBobT * 1.7) * 0.4;
        antWave = Math.sin(antennaT) * 3.5 + Math.sin(antennaT * 2.2) * 0.9;
        earWig = Math.sin(earWiggleT) * 1.8;
      }

      const cx = W / 2;
      const cy = H / 2 - 5 + bodyBob;
      const bodyX = cx - 38;
      const bodyY = cy - 28;
      const bodyW = 76;
      const bodyH = 64;
      const headX = cx - 30;
      const headY = cy - 70;
      const headW = 60;
      const headH = 52;

      // Legs
      drawRR(bodyX + 4, bodyY + 42, 18, 28, 6, '#1a5a5a', null);
      drawRR(bodyX + bodyW - 22, bodyY + 42, 18, 28, 6, '#1a5a5a', null);

      // Antenna
      ctx.beginPath();
      ctx.moveTo(cx, headY);
      ctx.lineTo(cx + antWave, headY - 22);
      ctx.strokeStyle = '#5dd4b4';
      ctx.lineWidth = 2.5;
      ctx.lineCap = 'round';
      ctx.stroke();

      ctx.beginPath();
      ctx.arc(cx + antWave, headY - 26, 5, 0, Math.PI * 2);
      ctx.fillStyle = '#5dd4b4';
      ctx.fill();

      // Ears
      const earLX = headX - 10;
      const earRX = headX + headW - 4;
      const earY = headY + 12;
      drawRR(earLX, earY + earWig * 0.5, 12, 22, 4, '#1a5a5a', null);
      drawRR(earLX + 2, earY + 3 + earWig * 0.5, 8, 14, 3, '#5dd4b4', null);
      drawRR(earRX, earY - earWig * 0.5, 12, 22, 4, '#1a5a5a', null);
      drawRR(earRX + 2, earY + 3 - earWig * 0.5, 8, 14, 3, '#5dd4b4', null);

      // Head
      drawRR(headX, headY, headW, headH, 14, '#1d7a7a', '#5dd4b4', 1.5);
      // Body
      drawRR(bodyX, bodyY, bodyW, bodyH, 10, '#1d7a7a', null);

      // AI badge
      ctx.fillStyle = 'rgba(255,255,255,0.08)';
      ctx.beginPath();
      ctx.arc(cx, bodyY + 15, 20, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#5dd4b4';
      ctx.font = '500 13px monospace';
      ctx.textAlign = 'center';
      ctx.fillText('AI', cx, bodyY + 20);

      // LEDs — smooth sine pulse
      const ledColors = ['#5dd4b4', '#4bbfff', '#c084fc'];
      for (let li = 0; li < 3; li++) {
        let p = 1.0;
        if (!prefersReducedMotion) {
          p = 0.55 + 0.45 * Math.sin(ledT * 1.6 + li * 1.8);
          p = p * p;
        }
        ctx.globalAlpha = p;
        ctx.fillStyle = ledColors[li];
        ctx.beginPath();
        ctx.arc(cx - 14 + li * 14, bodyY + 38, 4, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.globalAlpha = 1;

      // Eyes
      const eyeLX = cx - 16;
      const eyeRX = cx + 16;
      const eyeY = headY + 22;
      const eyeR = 9;

      let blinkScale = 1;
      if (!prefersReducedMotion) {
        blinkT++;
        if (!isBlinking && blinkT >= blinkNext) {
          isBlinking = true;
          blinkDur = 0;
          blinkNext = 80 + Math.random() * 120;
          blinkT = 0;
        }
        if (isBlinking) {
          blinkDur++;
          if (blinkDur > 12) isBlinking = false;
        }
        blinkScale = isBlinking ? Math.max(0.05, 1 - Math.sin((blinkDur / 12) * Math.PI)) : 1;
      }

      if (shieldAlpha > 0.3) {
        ctx.save();
        ctx.globalAlpha = Math.min(shieldAlpha, 1);
        ctx.fillStyle = '#1d7a7a';
        [eyeLX, eyeRX].forEach((ex) => {
          ctx.beginPath();
          ctx.ellipse(ex, eyeY, eyeR, eyeR * blinkScale, 0, 0, Math.PI * 2);
          ctx.fill();
          ctx.strokeStyle = '#5dd4b4';
          ctx.lineWidth = 1.5;
          ctx.stroke();
        });
        ctx.fillStyle = '#5dd4b4';
        ctx.font = '500 11px monospace';
        ctx.textAlign = 'center';
        ctx.fillText('* *', cx, eyeY + 4);
        ctx.restore();
      } else {
        // spring-physics eye tracking
        [eyeLX, eyeRX].forEach((ex, i) => {
          const cur = i === 0 ? eyeCurL : eyeCurR;
          let tgt = getEyeTarget(ex, eyeY);
          if (shieldAlpha > 0.01) {
            tgt = { x: 0, y: 0 };
          }
          if (prefersReducedMotion) {
            cur.x = tgt.x;
            cur.y = tgt.y;
          } else {
            cur.vx = cur.vx * 0.78 + (tgt.x - cur.x) * 0.16;
            cur.vy = cur.vy * 0.78 + (tgt.y - cur.y) * 0.16;
            cur.x += cur.vx;
            cur.y += cur.vy;
          }
          ctx.save();
          ctx.globalAlpha = 1 - shieldAlpha * 0.8;
          ctx.fillStyle = '#ffffff';
          ctx.save();
          ctx.translate(ex, eyeY);
          ctx.scale(1, blinkScale);
          ctx.beginPath();
          ctx.arc(0, 0, eyeR, 0, Math.PI * 2);
          ctx.restore();
          ctx.fill();

          ctx.fillStyle = '#0a3a3a';
          ctx.beginPath();
          ctx.arc(ex + cur.x, eyeY + cur.y * blinkScale, 4.5, 0, Math.PI * 2);
          ctx.fill();

          ctx.fillStyle = '#ffffff';
          ctx.beginPath();
          ctx.arc(ex + cur.x + 1.5, eyeY + cur.y * blinkScale - 1.5, 1.5, 0, Math.PI * 2);
          ctx.fill();
          ctx.restore();
        });
      }

      // Mouth
      const mouthY = headY + 41;
      ctx.beginPath();
      if (shieldAlpha > 0.5) {
        // flat line when covering eyes
        ctx.moveTo(cx - 7, mouthY);
        ctx.lineTo(cx + 7, mouthY);
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.stroke();

        // wave overlay
        ctx.save();
        ctx.globalAlpha = shieldAlpha * 0.9;
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        const sx = cx - 10;
        const sy = mouthY - 2;
        ctx.beginPath();
        ctx.moveTo(sx, sy);
        ctx.lineTo(sx + 6, sy);
        ctx.lineTo(sx + 9, sy - 3);
        ctx.lineTo(sx + 13, sy);
        ctx.lineTo(cx + 10, sy);
        ctx.stroke();
        ctx.restore();
      } else {
        ctx.arc(cx, mouthY - 3, 8, 0.15, Math.PI - 0.15);
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.stroke();
      }

      animationId = requestAnimationFrame(draw);
    };

    draw();

    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      if (animationId) {
        cancelAnimationFrame(animationId);
      }
    };
  }, [hideEyes]);

  return (
    <canvas
      id="aiChar"
      ref={canvasRef}
      width={180}
      height={180}
      style={{ display: 'block', maxWidth: '100%' }}
      aria-label="Interactive AI Assistant Robot illustration"
    />
  );
}

export default AiAssistantCanvas;
