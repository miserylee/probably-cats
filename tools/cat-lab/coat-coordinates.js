/* Attach pigment coordinates before length or body condition changes the
   surface. The two poses share muzzle, shoulder, hip, paw and tail landmarks. */
(() => {
  "use strict";
  const cache = new Map();
  const unit = n => Math.min(1, Math.max(0,n));

  function coordinates(mesh, baby) {
    const pos=mesh.geometry.getAttribute("position"), n=pos.count;
    const part=mesh.name==="Body and ruff" ? 0 : mesh.name==="tail" ? 3 :
      mesh.name==="foreleg" || mesh.name==="haunch" ? 2 : 1;
    const domain=new Float32Array(n*3), region=new Float32Array(n*3), pigment=new Float32Array(n*3);
    mesh.geometry.computeBoundingBox();
    const {min,max}=mesh.geometry.boundingBox;
    const endUnit=(v,a,b)=>unit((v-a)/Math.max(b-a,.001));
    for(let i=0;i<n;i++) {
      const x=pos.getX(i),y=pos.getY(i),z=pos.getZ(i);
      let h=endUnit(y,min.y,max.y);
      let u=x/(baby?.65:.85), v, w;
      if(part===1) {
        // Chin, forehead and ear tips occupy the same portion at both ages.
        h=baby?endUnit(y,.315,1.16):endUnit(y,1.99,3.15);
        u=x/(baby?.53:.70)*.80;
        v=.43+.57*h;
        w=.51+.49*(baby?endUnit(z,.41,1.33):endUnit(z,-.39,.80));
      } else if(part===0) {
        const sides=baby?28:24, angle=2*Math.PI*(i%sides)/sides;
        const dorsal=.5+.5*Math.cos(angle)*(baby?1:-1);
        const length=baby?endUnit(z,-.94,1.15):endUnit(y,.16,2.07);
        h=.15+.77*dorsal;
        v=.10+.60*dorsal+.06*length;
        w=-.89+1.5*length;
      } else if(part===2) {
        const forward=mesh.name==="foreleg";
        if(baby && forward) h=1-endUnit(z,.26,1.38);
        if(baby && !forward) h=.55*h+.45*(1-endUnit(z,-.82,.04));
        v=(forward?.62:.67)*h;
        w=forward?.62-.38*h:-.70+.49*(1-h);
      } else {
        const ring=Math.floor(i/16);
        h=ring/Math.max(1,n/16-1);
        // Uncurl the tail into the same domain for both life stages.
        w=-.87-.78*h;
        v=.29+.096*Math.sin(Math.PI*2*(i%16)/16);
        u=.30+.12*Math.cos(Math.PI*2*(i%16)/16);
      }
      domain.set([u,v,w],3*i);
      // A single tip-depth plane crosses the trunk, shoulders and legs. Each
      // pose has its own spinal pitch, anchored in the undeformed coordinates.
      const tipDepth=baby ? .92*y-.23*z+.10 : .29*y-.46*z+.04;
      region.set([part,h,tipDepth],3*i);
      // Islands span mesh boundaries. Use one rest-space volume on both
      // sides of the shoulder instead of restarting a field on each limb.
      const facialHeight = baby ? endUnit(y,.315,1.16) : endUnit(y,1.99,3.15);
      const facialDepth = baby ? endUnit(z,.41,1.33) : endUnit(z,-.39,.80);
      const face = (baby ? endUnit(y,.33,.65)*endUnit(z,.27,.69) :
                           endUnit(y,1.85,2.32)*endUnit(z,-.52,-.06));
      const wrist = baby ? 0 : endUnit(z,-.06,.42)*(1-endUnit(y,.38,1.16));
      const along = baby ? .83*z-.03 : 1.05*z+.31*y-.22+.54*wrist;
      let px=x/(baby?.65:.86);
      let py=(.10+.70*tipDepth)*(1-face)+(.43+.57*facialHeight)*face;
      let pz=along*(1-face)+(.51+.49*facialDepth)*face;
      if(part===3) {
        const tail=endUnit(h,0,.23);
        px+=(u-px)*tail; py+=(v-py)*tail; pz+=(w-pz)*tail;
      }
      pigment.set([px,py,pz],3*i);
    }
    return {domain,region,pigment};
  }
  function attach(T,root,stage,neutral,geometryKey=stage) {
    let reference=cache.get(geometryKey);
    if(!reference) {
      reference=[];
      neutral.traverse(node=>{if(node.isMesh) reference.push(coordinates(node,stage==="kitten"));});
      cache.set(geometryKey,reference);
    }
    let index=0;
    root.traverse(mesh=>{
      if(!mesh.isMesh)return;
      const saved=reference[index++];
      const n=mesh.geometry.getAttribute("position").count;
      const data=saved && saved.region.length===3*n ? saved : coordinates(mesh,stage==="kitten");
      mesh.geometry.setAttribute("coatDomain",new T.BufferAttribute(data.domain,3));
      mesh.geometry.setAttribute("pigmentDomain",new T.BufferAttribute(data.pigment,3));
      mesh.geometry.setAttribute("coatRegion",new T.BufferAttribute(data.region,3));
    });
  }
  window.CatCoordinates={has:key=>cache.has(key),attach};
})();
