/* The seated cat exposes the entire underside of its jaw. Continue the
   actual rim of the skull into a section cut through the shoulder skin. */
(() => {
  "use strict";
  const turn=2*Math.PI;
  const mix=(a,b,t)=>a+(b-a)*t;

  function openShoulders(T,mesh,height) {
    const old=mesh.geometry,pos=old.getAttribute("position"),tri=old.index.array;
    const extra=[],indices=[],crossings=new Map();
    function intersect(a,b) {
      const low=Math.min(a,b),high=Math.max(a,b),key=low+":"+high;
      if(crossings.has(key))return crossings.get(key);
      const t=(height-pos.getY(low))/(pos.getY(high)-pos.getY(low));
      const index=pos.count+extra.length;
      extra.push({low,high,t});
      crossings.set(key,index);
      return index;
    }
    for(let i=0;i<tri.length;i+=3) {
      const a=tri[i],b=tri[i+1],c=tri[i+2];
      const below=Number(pos.getY(a)<height)+Number(pos.getY(b)<height)+Number(pos.getY(c)<height);
      if(below===3)indices.push(a,b,c);
      if(below===0 || below===3)continue;
      const face=[a,b,c],loop=[];
      for(let k=0;k<3;k++) {
        const u=face[k],v=face[(k+1)%3],inside=pos.getY(u)<height;
        if(inside)loop.push(u);
        if(inside!==(pos.getY(v)<height))loop.push(intersect(u,v));
      }
      for(let j=2;j<loop.length;j++)indices.push(loop[0],loop[j-1],loop[j]);
    }
    const cut=new T.BufferGeometry();
    for(const [name,attr] of Object.entries(old.attributes)) {
      const size=attr.itemSize,data=new Float32Array((pos.count+extra.length)*size);
      data.set(attr.array);
      extra.forEach(({low,high,t},j)=>{
        for(let k=0;k<size;k++)data[(pos.count+j)*size+k]=mix(data[low*size+k],data[high*size+k],t);
      });
      cut.setAttribute(name,new T.BufferAttribute(data,size));
    }
    cut.setIndex(indices);cut.computeBoundingSphere();
    mesh.geometry=cut;old.dispose();
    const position=cut.getAttribute("position"),normal=cut.getAttribute("normal");
    return extra.map((_,j)=>{
      const i=pos.count+j;
      return {point:[position.getX(i),height,position.getZ(i)],normal:[normal.getX(i),normal.getY(i),normal.getZ(i)]};
    });
  }

  function order(rim) {
    const xs=rim.map(v=>v.point[0]),zs=rim.map(v=>v.point[2]);
    const x=.5*(Math.min(...xs)+Math.max(...xs)),z=.5*(Math.min(...zs)+Math.max(...zs));
    const ring=rim.map(v=>({...v,angle:(Math.atan2(v.point[0]-x,v.point[2]-z)+turn)%turn}));
    return ring.sort((a,b)=>a.angle-b.angle);
  }
  function sample(ring,angle) {
    const end=ring.findIndex(v=>v.angle>angle);
    const i=end<0?0:end,j=(i+ring.length-1)%ring.length,a=ring[j],b=ring[i];
    const t=((angle-a.angle+turn)%turn)/((b.angle-a.angle+turn)%turn);
    const at=key=>a[key].map((value,k)=>mix(value,b[key][k],t));
    return {point:at("point"),normal:at("normal")};
  }
  function bridge(T,skull,shoulders,fullness) {
    const upper=order(skull),lower=order(shoulders);
    const axis=k=>{
      const v=upper.map(p=>p.point[k]),lo=Math.min(...v),hi=Math.max(...v);
      return {center:(lo+hi)/2,radius:(hi-lo)/2};
    };
    const xAxis=axis(0),zAxis=axis(2);
    // A section through the cervical muscles sits behind the free chin.
    // Jaw width sets its frame. Body condition adds a little soft tissue;
    // the much wider shoulder section still carries the chest and upper back.
    const throatY=1.84;
    const condition=fullness-.5;
    const radiusX=.63*xAxis.radius+.034*condition;
    const radiusZ=.76*zAxis.radius+.024*condition;
    const shoulderCenter=k=>lower.reduce((sum,v)=>sum+v.point[k],0)/lower.length;
    const centerX=mix(shoulderCenter(0),xAxis.center,.64);
    const centerZ=mix(shoulderCenter(2),zAxis.center,.40);
    const pos=[],normal=[],indices=[],rings=[];
    // Keep both boundary polygons intact, even where the extracted chest has
    // a much denser ring than the muzzle. Intermediate sections are radial.
    const steps=20,sectors=112;
    const rays=new Map();
    function ray(angle) {
      if(rays.has(angle))return rays.get(angle);
      const a=sample(upper,angle),b=sample(lower,angle);
      const radial=[Math.sin(angle),Math.cos(angle)];
      const alignment=Math.max(.25,b.normal[0]*radial[0]+b.normal[2]*radial[1]);
      const endSlope=-b.normal[1]/alignment;
      const jawReach=1/Math.hypot(radial[0]/xAxis.radius,radial[1]/zAxis.radius);
      const throatReach=1/Math.hypot(radial[0]/radiusX,radial[1]/radiusZ);
      const data={a,b,radial,endSlope,jawReach,throatReach};
      rays.set(angle,data);
      return data;
    }
    function section(angle,t) {
      const {a,b,radial,endSlope,jawReach,throatReach}=ray(angle);
      const relax=Math.min(1,t/.43),oval=.89*relax*relax*(3-2*relax);
      const point=[0,mix(a.point[1],b.point[1],t),0];
      // Both Hermite spans meet the same oval with the same tangent. This
      // lets the nape lean toward the jaw as the shoulders broaden below.
      const upperHalf=point[1]>=throatY;
      const fromY=upperHalf?a.point[1]:throatY, toY=upperHalf?throatY:b.point[1];
      const dy=toY-fromY, u=(point[1]-fromY)/dy, u2=u*u, u3=u2*u;
      for(const [k,dir] of [[0,radial[0]],[2,radial[1]]]) {
        const middle=(k===0?centerX:centerZ)+throatReach*dir;
        const middleSlope=(k===2 ? .32 : 0)-.13*dir;
        const jaw=mix(a.point[k],(k===0?xAxis:zAxis).center+jawReach*dir,oval);
        const start=upperHalf?jaw:middle, end=upperHalf?middle:b.point[k];
        const startSlope=upperHalf?(.83+.40*radial[1])*dir:middleSlope;
        const finishSlope=upperHalf?middleSlope:endSlope*dir;
        point[k]=mix(start,end,3*u2-2*u3)
          +dy*((u3-2*u2+u)*startSlope+(u3-u2)*finishSlope);
      }
      return point;
    }
    for(let r=0;r<=steps;r++) {
      const boundary=r===0?upper:r===steps?lower:null;
      const ring=[],count=boundary?boundary.length:sectors,t=r/steps;
      for(let j=0;j<count;j++) {
        const angle=boundary?boundary[j].angle:turn*j/count;
        let point,n;
        if(boundary) {
          point=boundary[j].point;n=boundary[j].normal;
        } else {
          point=section(angle,t);
          const tangent=new T.Vector3().fromArray(section((angle+.026)%turn,t))
            .sub(new T.Vector3().fromArray(section((angle-.026+turn)%turn,t)));
          const down=new T.Vector3().fromArray(section(angle,t+.003))
            .sub(new T.Vector3().fromArray(section(angle,t-.003)));
          const modelNormal=down.cross(tangent).normalize();
          if(t<.21) {
            const w=1-t/.21;
            modelNormal.lerp(new T.Vector3().fromArray(ray(angle).a.normal),w*w).normalize();
          }
          n=modelNormal.toArray();
        }
        ring.push({angle,id:pos.length/3});pos.push(...point);normal.push(...n);
      }
      rings.push(ring);
    }
    // Zip neighbouring rings in angular order. Start at the last sample,
    // then walk over the zero-angle seam together.
    for(let row=1;row<rings.length;row++) {
      const a=rings[row-1],b=rings[row];
      let i=0,j=0,ai=a[a.length-1].id,bj=b[b.length-1].id;
      while(i<a.length || j<b.length) {
        if(j<b.length && (i===a.length || b[j].angle<=a[i].angle)) {
          indices.push(ai,bj,b[j].id);bj=b[j++].id;
        } else {
          indices.push(ai,bj,a[i].id);ai=a[i++].id;
        }
      }
    }
    const geometry=new T.BufferGeometry();
    geometry.setAttribute("position",new T.Float32BufferAttribute(pos,3));
    geometry.setAttribute("normal",new T.Float32BufferAttribute(normal,3));
    const count=pos.length/3;
    for(const key of ["frontUV","sideUV"])geometry.setAttribute(key,new T.BufferAttribute(new Float32Array(count*2),2));
    for(const key of ["coatDomain","pigmentDomain","coatRegion"])geometry.setAttribute(key,new T.BufferAttribute(new Float32Array(count*3),3));
    geometry.setAttribute("viewMix",new T.BufferAttribute(new Float32Array(count),1));
    geometry.setIndex(indices);geometry.computeBoundingSphere();
    return geometry;
  }
  function join(T,root,trunk,fullness) {
    let rim;
    root.traverse(mesh=>{if(mesh.userData.throatRim)rim=mesh.userData.throatRim;});
    if(!rim)return;
    const shoulders=openShoulders(T,trunk,1.54);
    const mesh=new T.Mesh(bridge(T,rim,shoulders,fullness),trunk.material);
    mesh.name="Bare throat and nape";root.add(mesh);
  }
  window.CatBareNeck={join};
})();
