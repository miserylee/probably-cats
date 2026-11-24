/* Stable atlas charts for a head that can turn through a full revolution. */
(() => {
  "use strict";

  // Continue the ginger/ink division of the crown over the occiput. This
  // band of the sheet is fur above the brows and between the ear bowls.
  function crown(lateral,h,kitten) {
    const t=Math.max(0,Math.min(1,h));
    const side=Math.min(1,Math.abs(lateral));
    const x=(kitten ? 382 : 327)+lateral*(kitten ? 84 : 71)+
      (kitten ? 23 : 19)*t+5*Math.sin(5.1*t-.4)*(1-side*side);
    const y=(kitten ? 205 : 136)+9*side+(33-16*side-7*Math.max(0,lateral))*t;
    return kitten ? [x/2172,1-y/724] : [x/1536,1-y/1024];
  }

  function temple(h) {
    return .64*Math.exp(-(((h-.76)/.27)**2));
  }

  function pinFace(vertex,x,y,t) {
    const h=(y-130)/234;
    // The last pixels along the painted silhouette contain antialiasing
    // against the transparent sheet. Keep those pixels off the exposed
    // temple when it is seen edge-on. The eye and muzzle stay on their UVs.
    const border=Math.max(0,(t-.80)/.20);
    const inward=-Math.sign(x-354)*border*border*(3-2*border);
    vertex.f=[vertex.f[0]+inward*10/1536,vertex.f[1]];
    const edge=Math.max(0,(t-.92)/.08);
    vertex.w=temple(h)*edge*edge*(3-2*edge);
    vertex.s=vertex.w>0 ? crown((x-354)/165,h,false) : vertex.f;
    vertex.profile=0;
    return vertex;
  }

  function napeBlend(h,depth) {
    // Keep the outer cheek at the front seam. Only this short return carries
    // its color; the rest of the rear skull uses the continuous crown chart.
    const jowl=Math.exp(-(((h-.72)/.24)**2));
    const t=Math.min(1,Math.max(0,(depth-.10*jowl)/(.45+.19*jowl)));
    return t*t*(3-2*t);
  }
  function pinNape(vertex,edgeUV,x,y,depth) {
    const h=(y-130)/234;
    const inset=-Math.sign(x-354);
    vertex.f=[edgeUV[0]+inset*depth*24/1536,edgeUV[1]+depth*6/1024];
    vertex.s=crown((x-354)/165,h,false);
    const seam=temple(h);
    vertex.w=seam+(1-seam)*napeBlend(h,depth);
    vertex.profile=0;
    return vertex;
  }
  window.CatFaceProjection={pinFace,pinNape,crown,napeBlend};
})();
